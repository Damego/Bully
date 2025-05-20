namespace Bully
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            connectBtn = new System.Windows.Forms.Button();
            label1 = new System.Windows.Forms.Label();
            nodeTB = new System.Windows.Forms.RichTextBox();
            statusTB = new System.Windows.Forms.RichTextBox();
            SuspendLayout();
            // 
            // connectBtn
            // 
            connectBtn.Location = new System.Drawing.Point(19, 78);
            connectBtn.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            connectBtn.Name = "connectBtn";
            connectBtn.Size = new System.Drawing.Size(106, 36);
            connectBtn.TabIndex = 5;
            connectBtn.Text = "Подключиться";
            connectBtn.UseVisualStyleBackColor = true;
            connectBtn.Click += connectBtn_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 204);
            label1.Location = new System.Drawing.Point(19, 9);
            label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(97, 20);
            label1.TabIndex = 4;
            label1.Text = "Номер узла";
            // 
            // nodeTB
            // 
            nodeTB.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 204);
            nodeTB.Location = new System.Drawing.Point(19, 37);
            nodeTB.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            nodeTB.Name = "nodeTB";
            nodeTB.Size = new System.Drawing.Size(106, 35);
            nodeTB.TabIndex = 3;
            nodeTB.Text = "";
            // 
            // statusTB
            // 
            statusTB.Enabled = false;
            statusTB.Location = new System.Drawing.Point(133, 5);
            statusTB.Name = "statusTB";
            statusTB.Size = new System.Drawing.Size(261, 140);
            statusTB.TabIndex = 6;
            statusTB.Text = "";
            // 
            // MainForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(397, 144);
            Controls.Add(statusTB);
            Controls.Add(connectBtn);
            Controls.Add(label1);
            Controls.Add(nodeTB);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "MainForm";
            Text = "Main";
            FormClosing += MainForm_FormClosing;
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button connectBtn;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.RichTextBox nodeTB;
        private System.Windows.Forms.RichTextBox statusTB;
    }
}