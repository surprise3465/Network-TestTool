using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

namespace PingTestTool
{
    public delegate void UpdateDelegate(string info);
    public partial class Form1 : Form
    {
        private string path = AppDomain.CurrentDomain.BaseDirectory;
        private UpdateDelegate updateUI = null;

        public class Device
        {
            public string Ip;
            public string Name;
            public PingResult pingResult;
            public Device(string ip, string name)
            {
                Ip = ip;
                Name = name;
                pingResult = new PingResult();
            }
        }

        private List<Device> deviceList = new List<Device>();
        private Thread[] thread;
        private Thread wait;
        private int time_out;
        private int Package_size;
        private int try_times;
        private int interval;
        public enum PingResultEntryStatus
        {
            Success,
            GenericFailureSeeReplyStatus,
            PingAbortedForHighNetworkUsage,
            PingAbortedUnableToGetNetworkUsage,
            ExceptionRaisedDuringPing
        };

        public class PingResultEntry
        {
            public double? Rtt { get; private set; }
            public IPStatus? IpStatus { get; private set; }
            public PingResultEntryStatus Status { get; private set; }
            public DateTime Time { get; private set; }

            public PingResultEntry(double? rtt, IPStatus? ipStatus, PingResultEntryStatus status, DateTime time)
            {
                IpStatus = ipStatus;
                Rtt = rtt;
                Status = status;
                Time = time;
            }
        }

        public class PingResult
        {
            public List<PingResultEntry> results;
            public int? err_num;
            public int? total_num;
            private double? avg;
            private double? speed;
            public PingResult()
            {
                results = new List<PingResultEntry>();
                err_num = 0;
                total_num = 0;
            }

            public void addPingResultEntry(PingResultEntry newEntry)
            {
                // Adding a new entry
                results.Add(newEntry);
                // Reset any previously calculated stats (they where probably already null but who knows...)
                avg = null;
                speed = null;
            }

            public double? getSpeed(int length)
            {
                // Calculating speed if not yet calculated
                if (avg <= 0) return 1024.0F;
                return (double)length/avg*1000/1024;
            }

            public double? getAvg()
            {
                // Calculating avg if not yet calculated
                if (avg == null && results.Count(res => res.Status == PingResultEntryStatus.Success) > 0)
                {
                    avg = 0;
                    int cont = 0;
                    foreach (var e in results.Where(x => x.Status == PingResultEntryStatus.Success))
                    {
                        cont++;
                        avg += e.Rtt;
                    }
                    if (cont > 0)
                    {
                        avg = avg / cont;
                    }
                }
                return avg;
            }
        }

        public Form1()
        {
            InitializeComponent();
            textBox1.Text = path + "ConfigData.xml";

            textBox_timeout.Text = "300";
            textBox_trytimes.Text = "200";
            textBox_packetsize.Text = "4096";
            checkBox1.Checked = false;
            textBox_interval.Text = "100";
            LoadXmlFile(textBox1.Text);

            updateUI = UpdateList;  
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (DialogResult.OK == openFileDialog1.ShowDialog())
            {
                textBox1.Text = openFileDialog1.FileName;
                LoadXmlFile(textBox1.Text);
            }
        }

        private void LoadXmlFile(string fileName)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(fileName);
                deviceList.Clear();
                XmlNodeList list = doc.SelectNodes("/Root/Project/DeviceList/Device");
                foreach (XmlNode n in list)
                {
                    deviceList.Add(new Device(n.Attributes["ip"].Value, n.Attributes["name"].Value));                   
                }
                foreach (var dev  in deviceList)
                {
                    int i = dataGridView1.Rows.Add();
                    dataGridView1.Rows[i].Cells[0].Value = dev.Name;
                    dataGridView1.Rows[i].Cells[1].Value = dev.Ip;
                }
                dataGridView1.ClearSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void button_begin_Click(object sender, EventArgs e)
        {
            if (deviceList.Count == 0)
            {
                MessageBox.Show("There is no device！");
                return;
            }

            if (button_begin.Text == "Stop")
            {
                wait?.Abort();
                wait = null;
                for (var i = 0; i < thread.Length; i++)
                {
                    if (thread[i] == null || !thread[i].IsAlive) continue;
                    thread[i].Abort();
                    thread[i] = null;
                }
                button_begin.Text = "Begin";
                textBox_packetsize.Enabled = true;
                textBox_timeout.Enabled = true;
                textBox_trytimes.Enabled = true;
                checkBox1.Enabled = true;
                button_clear.Enabled = true;
                textBox_interval.Enabled = true;
            }
            else
            {
                button_begin.Text = "Stop";
                textBox_packetsize.Enabled = false;
                textBox_timeout.Enabled = false;
                textBox_trytimes.Enabled = false;
                textBox_interval.Enabled = false;
                checkBox1.Enabled = false;
                button_clear.Enabled = false;

                Package_size = Convert.ToInt32(textBox_packetsize.Text);
                time_out = Convert.ToInt32(textBox_timeout.Text);
                try_times = Convert.ToInt32(textBox_trytimes.Text);
                interval = Convert.ToInt32(textBox_interval.Text);
                thread = new Thread[deviceList.Count];
                for (var i = 0; i < deviceList.Count; i++)
                {
                    thread[i] = new Thread(Threadhandler) {IsBackground = true};
                    thread[i].Start(i);
                }

                wait= new Thread(waitforallthread) { IsBackground = true };
                wait.Start();
            }
        }

        private void waitforallthread()
        {
            bool flag = true;
            Thread.Sleep(200);
            while (flag)
            {
                flag = false;
                foreach (var thr in thread.Where(thr => thr.IsAlive))
                {
                    Thread.Sleep(2000);
                    flag = true;
                }
            }

            BeginInvoke(updateUI, "button");
        }

        private void Threadhandler(object index)
        {           
            Ping pingSender = new Ping();
            PingOptions options = new PingOptions();
            byte[] buffer = new byte[Package_size];
            options.DontFragment = checkBox1.Checked;
            for (var i = 0; i < try_times; i++)
            {
                Device dev = deviceList[(int)index];
                try
                {
                    PingReply reply = pingSender.Send(dev.Ip, time_out, buffer, options);
                    if (reply.Status == IPStatus.Success)
                    {
                        // All has gone well
                        dev.pingResult.addPingResultEntry(new PingResultEntry(
                            reply.RoundtripTime, reply.Status, PingResultEntryStatus.Success, System.DateTime.Now));

                    }
                    else
                    {
                        // Something went wrong, wrong but "expected"
                        dev.pingResult.addPingResultEntry(new PingResultEntry(
                            reply.RoundtripTime, reply.Status, PingResultEntryStatus.GenericFailureSeeReplyStatus,
                            System.DateTime.Now));
                        dev.pingResult.err_num++;
                    }
                    dev.pingResult.total_num++;
                    dev.pingResult.getAvg();
                    dev.pingResult.getSpeed(Package_size);
                }
                catch
                {
                    dev.pingResult.addPingResultEntry(new PingResultEntry(
                                    null, null, PingResultEntryStatus.ExceptionRaisedDuringPing, System.DateTime.Now));
                }

                BeginInvoke(updateUI, "list"+index);

                Thread.Sleep(interval);
            }
        }

        private void UpdateList(string info)
        {
            if (info.Contains("list"))
            {
                int i = Convert.ToInt32(info.Remove(0,4));
                Device dev = deviceList[i];
                dataGridView1.Rows[i].Cells[0].Value = dev.Name;
                dataGridView1.Rows[i].Cells[1].Value = dev.Ip;
                var ipStatus = dev.pingResult.results.Last().IpStatus;
                if (ipStatus != null)
                    dataGridView1.Rows[i].Cells[2].Value = ipStatus.ToString();

                var avg = dev.pingResult.getAvg();
                if (avg != null)
                    dataGridView1.Rows[i].Cells[3].Value = Math.Round((double) avg, 2).ToString();
                var speed = dev.pingResult.getSpeed(Package_size);
                if (speed != null)
                    dataGridView1.Rows[i].Cells[4].Value = Math.Round((double)speed, 2).ToString();
                dataGridView1.Rows[i].Cells[5].Value = dev.pingResult.err_num.ToString();
                dataGridView1.Rows[i].Cells[6].Value = dev.pingResult.total_num.ToString();
                
                dataGridView1.Refresh();
                dataGridView1.ClearSelection();
            }
            else if(info == "button")
            {
                button_begin.Text = "Begin";
                textBox_packetsize.Enabled = true;
                textBox_timeout.Enabled = true;
                textBox_trytimes.Enabled = true;
                checkBox1.Enabled = true;
                button_clear.Enabled = true;
                textBox_interval.Enabled = true;
            }
        }

        private void button_clear_Click(object sender, EventArgs e)
        {
            foreach(var dev in deviceList)
            {
                dev.pingResult.results.Clear();
                dev.pingResult.err_num = 0;
                dev.pingResult.total_num = 0;
            }
            dataGridView1.ResetText();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            dataGridView1.ClearSelection();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            wait?.Abort();
            wait = null;
            for (var i = 0; i < thread.Length; i++)
            {
                if (thread[i] == null || !thread[i].IsAlive) continue;
                thread[i].Abort();
                thread[i] = null;
            }

        }
    }
}
