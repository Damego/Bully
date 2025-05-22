using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using YamlDotNet.Serialization;

namespace Bully
{
    public partial class MainForm : Form
    {
        private int? nodeId; // Текущий номер узла
        private int? port; // Текущий порт узла
        private List<Dictionary<string, int>> allNodes; // Список всех зарегистрированных узлов
        private TcpListener listener; // Слушатель подключений к узлу
        private Thread connectionsReceiverThread; // Поток для слушателя
        private List<Node> connectedNodes = new(); // Список подключенных узлов. В конечном итоге должен совпадать со список зарегистрированных узлов
        private bool _continue = true; // Флаг на продолжение работы программы
        private int highestNodeId; // Самый старший возможный узел
        private Node coordinatorNode; // Узел, который является координатором
        private Task coordinatorPingTask; // Таска на пинг координатора
        private bool ponged; // Флаг, означающий, был ли принято сообщение PONG от координатора
        private bool receivedOk; // Флаг, означающий, был ли принято сообщение OK хотя бы от одного узла
        private List<UnregisteredConnection> unregisteredConnections = new(); // Незарегистрированные соединения. Ожидаем сообщения HELLO

        public MainForm()
        {
            InitializeComponent();
            allNodes = ReadNodes();

            var highestNode = allNodes.OrderBy(node => node["id"]).Last();
            highestNodeId = highestNode["id"];
        }

        private List<Dictionary<string, int>> ReadNodes()
        {
            string nodesInfo = File.ReadAllText("./nodes.yaml");
            var deserializer = new DeserializerBuilder().Build();
            return deserializer.Deserialize<List<Dictionary<string, int>>>(nodesInfo);
        }

        private int GetPortByNodeId(int nodeId)
        {
            return allNodes.Find(node => node["id"] == nodeId)["port"];
        }

        private IPAddress GetIPAddress()
        {
            IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress IP = hostEntry.AddressList[0];
            foreach (IPAddress address in hostEntry.AddressList)
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    IP = address;
                    break;
                }
            return IP;
        }

        private void ReceiveConnections()
        {
            while (_continue)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    string unregid = Guid.NewGuid().ToString();

                    // Приняли подключение и ожидаем сообщения HELLO с номером узла для регистрации подключения
                    unregisteredConnections.Add(
                        new UnregisteredConnection(
                            unregid,
                            client,
                            Task.Factory.StartNew(() => ReceiveMessages(client.Client, unregisteredConnectionId: unregid))
                        )
                    );
                }
                catch (Exception ex)
                {
                }
            }
        }

        private async Task ReceiveMessages(Socket socket, string unregisteredConnectionId)
        {
            int? _nodeId = null; // Если соединение незарегистрированное, то id узла ещё нет 

            while (_continue)
            {
                byte[] buff = new byte[1024];
                Debug.WriteLine("Awaiting a message");
                try
                {
                    await socket.ReceiveAsync(buff);
                }
                catch (SocketException ex) {
                    Debug.WriteLine($"Ошибка принятия сообщения: {ex.Message}");
                    if (_nodeId != null)
                    {
                        DeleteNode((int)_nodeId);
                        return;
                    }
                    return;
                    // Exception thrown: 'System.Net.Sockets.SocketException' in System.Net.Sockets.dll
                }
                // пустая строка, а после Exception thrown: 'System.IndexOutOfRangeException' in Bully.dll
                string rawMessage = Encoding.Unicode.GetString(buff);
                Debug.WriteLine($"RAW {rawMessage}");
                if (rawMessage.Length == 0)
                {
                    Debug.WriteLine($"Ошибка парсинга сообщения: `{rawMessage}`");
                    DeleteNode((int)_nodeId);
                    return;
                }
                // TODO: хуйня какаято я ебал
                // 2 узел отбирает координатор у 3го, хз почему
                string[] parts = rawMessage.Split(";");
                string message = parts[0];
                int nodeId = int.Parse(parts[1]);
                _nodeId = nodeId;

                await ProcessMessage(message, nodeId, unregisteredConnectionId);
            }
        }

        private async Task ProcessMessage(string message, int senderNodeId, string unregisteredConnectionId)
        {
            var senderNode = connectedNodes.Find(node => node.Id == senderNodeId);

            if (senderNode == null && unregisteredConnectionId == null)
            {
                return;
            }

            AddStatusText(message, senderNodeId);

            // Узел устроил выборы на роль координатора.
            // Если текущий узел живой, то он принимает это сообщение и отправляет ОК и сам начинает выборы.
            if (message == "ELECTION")
            {
                await SendMessageToNode(senderNode, "OK");
                await SendElectionMessage();
            }
            // Узел получил сообщение ОК от старших узлов и больше не участвует в выборах
            else if (message == "OK")
            {
                receivedOk = true;
            }
            // Один из узлов назначил себя координатором
            else if (message == "COORDINATOR")
            {
                if (coordinatorPingTask != null)
                {
                    coordinatorPingTask.Dispose();
                }
                coordinatorNode = senderNode;
                coordinatorPingTask = Task.Factory.StartNew(PingCoordinator);
            }
            // Узел проверяет, что координатор живой
            else if (message == "PING")
            {
                await SendMessageToNode(senderNode, "PONG");
            }
            // Координатор ответил узлу, что он живой
            else if (message == "PONG")
            {
                ponged = true;
            }
            // Приветственное сообщение от узла, который подключается к текущему узлу.
            // Оно необходимо, так как к TcpClient не привязывается порт удалённого узла и поэтому текущий узел не может
            // распознать кто к нему подключился.
            // В этом случае это подключение временно попадает в список незарегистрированных, а после принятия HELLO
            // отправляется в список зарегистрированных узлов
            else if (message == "HELLO")
            {
                RegisterConnection(unregisteredConnectionId, senderNodeId);
            }
        }

        private void RegisterConnection(string connectionId, int nodeId)
        {
            var connection = unregisteredConnections.Find(con => con.Id == connectionId);
            unregisteredConnections.Remove(connection);
            var node = connection.Register(nodeId);

            // Узел переподключился
            var existingNode = connectedNodes.Find(node =>  node.Id == nodeId);
            if (existingNode != null)
            {
                existingNode.Stop();
                existingNode.ReceiverTask = node.ReceiverTask;
                existingNode.Client = node.Client;
            }
            else
                AddNode(node);
        }

        /*
        Отправляет всем старшим узлам сообщение ELECTION.
        */
        private async Task SendElectionMessage()
        {
            Debug.WriteLine("Отправка ELECTION");
            coordinatorNode = null;
            receivedOk = false;
            if (coordinatorPingTask != null)
            {
                coordinatorPingTask.Dispose();
                coordinatorPingTask = null;
            }
            

            foreach (var node in connectedNodes)
            {
                if (node.Id < nodeId) continue;
                await SendMessageToNode(node, "ELECTION");
            }

            // Не блокируем асинхронный поток
#pragma warning disable CS4014
            Task.Factory.StartNew(async () =>
            {
                // Ожидаем в течение двух секунд ответа ОК хотя бы от одного узла. Если ответ не пришёл, то объявляем себя координатором
                await Task.Delay(3000);
                Debug.WriteLine("Прошло 3 сек. Отправка КООРДИНАТОР");
                if (!receivedOk)
                {
                    receivedOk = false;
                    await SendCoordinatorMessage();
                }
            });
#pragma warning restore CS4014
        }

        private async Task SendCoordinatorMessage()
        {
            if (coordinatorNode != null)
            {
                coordinatorPingTask.Dispose();
                coordinatorNode = null;
            }

            foreach (var node in connectedNodes)
            {
                await SendMessageToNode(node, "COORDINATOR");
            }
        }

        private async Task SendMessageToNode(Node node, string message)
        {
            Debug.WriteLine($"Connected count: {connectedNodes.Count}");
            Debug.WriteLine($"Попытка отправки {message}:{node.Id}");
            byte[] buff = Encoding.Unicode.GetBytes($"{message};{nodeId}");
            try
            {
                await node.Client.Client.SendAsync(buff);
                AddStatusText($"{message} -> {node.Id}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка отправки {message}:{node.Id}");
                // Узел перестал быть доступным. Удаляем его из списка зарегистрированных узлов.
                DeleteNode(node);
            }
        }

        /*
        Для того, чтобы понять, что координатор вообще живой, текущий узел будет с некоторой периодичностью отправлять ему сообщение.
        Если ответ не поступит, то узел начнёт ELECTION
        */
        private async Task PingCoordinator()
        {
            while (true)
            {
                if (coordinatorNode == null)
                {
                    Debug.WriteLine("Координатор не найден");
                    return;
                }
                ponged = false;
                await SendMessageToNode(coordinatorNode, "PING");

                await Task.Delay(4000);
                if (!ponged)
                {
                    await SendElectionMessage();
                    return;
                }
            }
        }

        private async Task ConnectToAllNodes()
        {
            foreach (var node in allNodes.Where(node => node["id"] != nodeId))
            {
                int id = node["id"];
                try
                {
                    AddStatusText($"Попытка подключиться к {id}");
                    await ConnectToNode(node["id"], node["port"]);
                }
                catch (Exception ex)
                {
                    AddStatusText($"Не удалось подключиться к {id}");
                    Debug.WriteLine(ex.Message);
                }
            }
        }

        // Подключается к узлу и отправляет приветственное сообщение
        private async Task ConnectToNode(int connectNodeId, int connectPort)
        {
            var ip = GetIPAddress();
            var client = new TcpClient();
            await client.ConnectAsync(ip.ToString(), connectPort);
            var node = AddNode(connectNodeId, client);
            await SendMessageToNode(node, "HELLO");
        }

        // Удаляет узел из памяти
        private void DeleteNode(int nodeId)
        {
            var node = connectedNodes.Find(node => node.Id == nodeId);
            if (node != null)
                DeleteNode(node);
        }

        private void DeleteNode(Node node)
        {
            connectedNodes.Remove(node);
            node.Stop();
            Debug.WriteLine($"Удалён {node.Id}");
        }

        private Node AddNode(Node node)
        {
            connectedNodes.Add(node);
            return node;
        }

        private Node AddNode(int nodeId, TcpClient client)
        {
            var receiverTask = Task.Factory.StartNew(() => ReceiveMessages(client.Client, null));
            var node = new Node(nodeId, client, receiverTask);
            connectedNodes.Add(node);
            return node;
        }

        private void AddStatusText(string text)
        {
            this.Invoke(new MethodInvoker(() =>
            {
                statusTB.Text = $"{text}\n{statusTB.Text}";
            }));
        }

        private void AddStatusText(string text, int nodeId)
        {
            this.Invoke(new MethodInvoker(() =>
            {
                statusTB.Text = $"{nodeId} -> {text}\n{statusTB.Text}";
            }));
        }

        private async Task Connect()
        {
            if (nodeId != null)
            {
                Disconnect();

                this.Invoke(new MethodInvoker(() =>
                {
                    connectBtn.Text = "Подключиться";
                    nodeTB.Enabled = true;
                }));

                return;
            }

            nodeId = int.Parse(nodeTB.Text);
            port = GetPortByNodeId((int)nodeId);
            _continue = true;

            try
            {
                listener = new TcpListener(GetIPAddress(), (int)port);
                listener.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
            connectionsReceiverThread = new Thread(ReceiveConnections);
            connectionsReceiverThread.Start();

            await ConnectToAllNodes();

            // Текущий узел является самым старшим среди всех остальных узлов, поэтому с чистой совестью он объявляет себя координатором
            if (nodeId == highestNodeId)
                await SendCoordinatorMessage();
            else
                await SendElectionMessage();

            this.Invoke(new MethodInvoker(() =>
            {
                connectBtn.Text = "Отключиться";
                nodeTB.Enabled = false;
            }));
        }

        private void connectBtn_Click(object sender, EventArgs e)
        {
            Connect();
        }

        private void Disconnect()
        {
            nodeId = null;
            port = null;
            _continue = false;

            foreach (var node in connectedNodes)
            {
                node.Client.Close();
            }
            connectedNodes.Clear();
            if (coordinatorPingTask != null)
            {
                coordinatorPingTask.Dispose();
                coordinatorPingTask = null;
            }

            if (listener != null)
            {
                listener.Stop();
                listener = null;
            }
            if (connectionsReceiverThread != null)
            {
                connectionsReceiverThread.Interrupt();
                connectionsReceiverThread.Join();
                connectionsReceiverThread = null;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Disconnect();
        }
    }

    class Node
    {
        public int Id { get; set; }
        public TcpClient Client { get; set; }
        public Task ReceiverTask { get; set; }

        public Node(int id, TcpClient client, Task receiverTask)
        {
            Id = id;
            Client = client;
            ReceiverTask = receiverTask;
        }

        public void Stop()
        {
            ReceiverTask.Dispose();
            Client.Close();
            Client.Dispose();
        }
    }

    class UnregisteredConnection
    {
        public string Id { get; set; }
        public TcpClient tcpClient { get; set; }
        public Task ReceiverTask { get; set; }

        public UnregisteredConnection(string id, TcpClient _tcpClient, Task receiverTask)
        {
            Id = id;
            tcpClient = _tcpClient;
            ReceiverTask = receiverTask;
        }

        public Node Register(int nodeId)
        {
            return new Node(nodeId, tcpClient, ReceiverTask);
        }

    }
}
