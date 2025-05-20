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
using YamlDotNet.Serialization;

namespace Bully
{
    public partial class MainForm : Form
    {
        private int? nodeId; // Текущий номер узла
        private int? port; // Текущий порт узла
        private List<Dictionary<string, int>> allNodes; // Список всех зарегистрированных узлов
        private TcpListener listener; // Слушатель подключений к узлу
        private Thread listenerThread; // Поток для слушателя
        private List<Node> connectedNodes = new(); // Список подключенных узлов. В конечном итоге должен совпадать со список зарегистрированных узлов
        private bool _continue = true; // Флаг на продолжение работы программы
        private int highestNodeId; // Самый старший возможный узел
        private Node coordinatorNode; // Узел, который является координатором
        private Task coordinatorPingTask; // Таска на пинг координатора
        private bool ponged; // Флаг, означающий, был ли принято сообщение PONG от координатора
        private bool receivedOk; // Флаг, означающий, был ли принято сообщение OK хотя бы от одного узла

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

        private int GetNodeIdByPort(int port)
        {
            return allNodes.Find(node => node["port"] == port)["id"];
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

        private async Task ReceiveConnections()
        {
            while (_continue)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    // Приняли подключение и ожидаем сообщения HELLO с номером узла
                }
                catch (Exception ex)
                {
                    if (!_continue)
                        return;
                    continue;
                }
            }
        }

        private async Task ReceiveMessages(Socket socket)
        {
            while (_continue)
            {
                byte[] buff = new byte[1024];
                await socket.ReceiveAsync(buff);

                string[] rawMessage = Encoding.Unicode.GetString(buff).Split(";");
                string message = rawMessage[0];
                int nodeId = int.Parse(rawMessage[1]);

                await ProcessMessage(message, nodeId);
            }
        }

        private async Task ProcessMessage(string message, int senderNodeId)
        {
            var senderNode = connectedNodes.Find(node => node.Id == senderNodeId);

            if (message == "ELECTION")
            {
                AddStatusText("Получено ELECTION", senderNodeId);
                // Младшие узлы отправляют старшим сообщение ELECTION
                // Если старший узел живой, то он отправляет ELECTION другим, более старшим узлам
                await SendMessage(senderNode.Client, "OK");
                await SendElectionMessage();
            }
            else if (message == "OK")
            {
                AddStatusText("Получено OK", senderNodeId);
                receivedOk = true;
            }
            else if (message == "COORDINATOR")
            {
                AddStatusText("Получено COORDINATOR", senderNodeId);
                coordinatorNode = senderNode;
                coordinatorPingTask = Task.Factory.StartNew(PingCoordinator);
            }
            else if (message == "PING")
            {
                AddStatusText("Получено PING", senderNodeId);
                await SendMessage(senderNode.Client, "PONG");
            }
            else if (message == "PONG")
            {
                AddStatusText("Получено PONG", senderNodeId);
                ponged = true;
            }
            else if (message == "HELLO")
            {

            }
        }

        /*
        Отправляет всем старшим узлам сообщение ELECTION.
        */
        private async Task SendElectionMessage()
        {
            coordinatorNode = null;
            receivedOk = false;

            foreach (var node in connectedNodes)
            {
                if (node.Id > nodeId) continue;

                await SendMessage(node.Client, "ELECTION");
            }

            // Ожидаем в течение двух секунд ответа ОК хотя бы от одного узла. Если ответ не пришёл, то объявляем себя координатором
            await Task.Delay(2000);
            if (!receivedOk)
            {
                receivedOk = false;
                await SendCoordinatorMessage();
            }
        }

        private async Task SendCoordinatorMessage()
        {
            foreach (var node in connectedNodes)
            {
                await SendMessage(node.Client, "COORDINATOR");
            }
        }

        private async Task SendMessage(TcpClient client, string message)
        {
            byte[] buff = Encoding.Unicode.GetBytes(message);
            await client.Client.SendAsync(buff);
        }

        /*
        Для того, чтобы понять, что координатор вообще живой, текущий узел будет с некоторой периодичностью отправлять ему сообщение.
        Если ответ не поступит, то узел начнёт ELECTION
        */
        private async Task PingCoordinator()
        {
            while (true)
            {
                ponged = false;
                await SendMessage(coordinatorNode.Client, "PING");

                await Task.Delay(2 * 1000);
                if (!ponged)
                {
                    await SendElectionMessage();
                    return;
                }
            }
        }

        private void ConnectToAllNodes()
        {
            foreach (var node in allNodes.Where(node => node["id"] != nodeId))
            {
                int id = node["id"];
                try
                {
                    AddStatusText($"Попытка подключиться к {id}");
                    ConnectToNode(node["id"], node["port"]);
                }
                catch (Exception ex)
                {
                    AddStatusText($"Не удалось подключиться к {id}");
                    Debug.WriteLine(ex.Message);
                    Thread.Sleep(100);
                }
            }
        }

        private async Task ConnectToNode(int connectNodeId, int connectPort)
        {
            var ip = GetIPAddress();
            
            var client = new TcpClient(ip.ToString(), connectPort);
            await SendMessage(client, $"HELLO;{(int)nodeId}");
        }

        private void AddNode(int nodeId, TcpClient client)
        {
            var receiverTask = Task.Factory.StartNew(() => ReceiveMessages(client.Client, nodeId));
            var node = new Node(nodeId, client, receiverTask);
            connectedNodes.Add(node);
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
                statusTB.Text = $"{nodeId}: {text}\n{statusTB.Text}";
            }));
        }

        private void connectBtn_Click(object sender, EventArgs e)
        {
            if (nodeId != null)
            {
                Disconnect();

                this.Invoke(new MethodInvoker(() => {
                    connectBtn.Text = "Подключиться";
                    nodeTB.Enabled = true;
                }));

                return;
            }

            nodeId = int.Parse(nodeTB.Text);
            port = GetPortByNodeId((int)nodeId);

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
            listenerThread = new Thread(ReceiveConnections);
            listenerThread.Start();

            ConnectToAllNodes();

            // Текущий узел является самым старшим среди всех остальных узлов, поэтому с чистой совестью он объявляет себя координатором
            if (nodeId == highestNodeId)
                SendCoordinatorMessage();
            else
                SendElectionMessage();

            this.Invoke(new MethodInvoker(() => {
                connectBtn.Text = "Отключиться";
                nodeTB.Enabled = false;
            }));
        }

        private void Disconnect()
        {
            nodeId = null;
            port = null;
            _continue = false;

            foreach (var node in connectedNodes)
            {
                node.ReceiverTask.Dispose();
                node.Client.Close();
            }
            if (coordinatorPingTask != null)
            {
                coordinatorPingTask.Dispose();
            }

            if (listener != null)
                listener.Stop();
            if (listenerThread != null)
                listenerThread.Interrupt();
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
    }
}
