using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using StackExchange.Redis;
using System.Data.SQLite;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;
using System.Configuration;

namespace DataSolving
{
    public partial class Form1 : Form
    {
        private ConnectionMultiplexer redis;
        private IDatabase db;
        private ISubscriber sub;

        private string stamp;
        //private List<DataStore> dataStoreCollection; 
        private int index;
        private ConcurrentQueue<DataValue> dataQueue;
        private BackgroundWorker backgroundWorkerSaveData;
        private bool saveDataSuccess;

        private List<DataStore> dataStoreCollection;

        public string databaseIp;
        public string databaseUser;
        public string databasePwd;
        public string databaseName;
        public string databaseTable;
        public string redisServerIp;
        private int redisDbIndex;

        private SimpleStore hts;

        public Form1()
        {
            InitializeComponent();

            databaseIp = ConfigurationManager.AppSettings["DatabaseIP"];
            databaseName = ConfigurationManager.AppSettings["DataBase"];
            databaseUser = ConfigurationManager.AppSettings["UserName"];
            databasePwd = ConfigurationManager.AppSettings["Password"];
            databaseTable = ConfigurationManager.AppSettings["Table"];

            redisDbIndex = int.Parse(ConfigurationManager.AppSettings["RedisDbIndex"]);
            redisServerIp = ConfigurationManager.AppSettings["RedisServerIP"];

            //redis = ConnectionMultiplexer.Connect("localhost,abortOnConnect=false");
            string redisConnString = redisServerIp + ",abortConnect=false";
            redis = ConnectionMultiplexer.Connect(redisConnString);
            //db = redis.GetDatabase(0);

            dataQueue = new ConcurrentQueue<DataValue>();
            stamp = "";

            dataStoreCollection = new List<DataStore>();

            index = 0;
            LoadChannelsSQL();
            backgroundWorkerSaveData = new BackgroundWorker();
            backgroundWorkerSaveData.WorkerSupportsCancellation = true;
            backgroundWorkerSaveData.DoWork += BackgroundWorkerSaveData_DoWork;
            saveDataSuccess = true;

            //hts = new SimpleStore(tempChannels, 60*10, redis,textBoxLog);

            buttonStart.Enabled = true;
            buttonStop.Enabled = false;
        }

        private void LoadChannelsSQL()
        {
            string connectionString = "Data Source = " + databaseIp + ";Network Library = DBMSSOCN;Initial Catalog = " + databaseName + ";User ID = " + databaseUser + ";Password = " + databasePwd;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    return;
                }

                List<int> periods = new List<int>();

                string strainStatement = "SELECT DISTINCT period from DataStoreConfig";
                SqlCommand command1 = new SqlCommand(strainStatement, connection);
                using (SqlDataReader reader1 = command1.ExecuteReader())
                {
                    while (reader1.Read())
                    {
                        int period = reader1.GetInt32(0);
                        periods.Add(period);
                    }
                }

                foreach (int period in periods)
                {
                    List<string> keys = new List<string>();
                    strainStatement = "select RedisKey,Period,Description from DataStoreConfig where Period =" + period+" order by RedisKey";
                    command1 = new SqlCommand(strainStatement, connection);
                    using (SqlDataReader reader1 = command1.ExecuteReader())
                    {
                        while (reader1.Read())
                        {
                            string key = reader1.GetString(0);
                            int p = reader1.GetInt32(1);
                            Object desc = reader1.GetValue(2);
                            keys.Add(key);

                            string[] viewItem = { key, p.ToString(), desc.ToString() };
                            ListViewItem listItem = new ListViewItem(viewItem);
                            this.listView1.Items.Add(listItem);
                        }
                    }
                    SimpleStore ss = new SimpleStore(keys, period, redis, this.redisDbIndex,dataQueue, textBoxLog);
                    dataStoreCollection.Add(ss);
                }
                connection.Close();
            }
        }

        private void LoadChannels()
        {
            using (SQLiteConnection connection = new SQLiteConnection("Data Source = config.db"))
            {
                connection.Open();
                

                List<int> periods = new List<int>();

                string strainStatement = "SELECT DISTINCT period from DataStoreConfig";
                SQLiteCommand command1 = new SQLiteCommand(strainStatement, connection);
                using (SQLiteDataReader reader1 = command1.ExecuteReader())
                {
                    while (reader1.Read())
                    {
                        int period = reader1.GetInt32(0);
                        periods.Add(period);
                    }
                }

                foreach(int period in periods)
                {
                    List<string> keys = new List<string>();
                    strainStatement = "select Key from DataStoreConfig where Period ="+period;
                    command1 = new SQLiteCommand(strainStatement, connection);
                    using (SQLiteDataReader reader1 = command1.ExecuteReader())
                    {
                        while (reader1.Read())
                        {
                            string key = reader1.GetString(0);
                            keys.Add(key);
                            //string desc = reader1.GetString(2);
                        }
                    }
                    SimpleStore ss = new SimpleStore(keys, period, redis, this.redisDbIndex, dataQueue, textBoxLog);
                    dataStoreCollection.Add(ss);
                } 

                strainStatement = "select ip,user,password,database,tableName from dbconfig";
                SQLiteCommand command2 = new SQLiteCommand(strainStatement, connection);
                using (SQLiteDataReader reader2 = command2.ExecuteReader())
                {
                    while (reader2.Read())
                    {
                        //string  groupId = reader.GetString(0);
                        databaseIp = reader2.GetString(0);
                        databaseUser = reader2.GetString(1);
                        databasePwd = reader2.GetString(2);
                        databaseName = reader2.GetString(3);
                        databaseTable = reader2.GetString(4);
                    }
                }
                connection.Close();
            }
        }

        public void UpdateDatabaseSetting(string ip,string user,string pwd,string database,string table)
        {
            this.databaseIp = ip;
            this.databaseUser = user;
            this.databasePwd = pwd;
            this.databaseName = database;
            this.databaseTable = table;

            using (SQLiteConnection connection = new SQLiteConnection("Data Source = config.db"))
            {
                connection.Open();
                string strainStatement = "delete from dbconfig";
                SQLiteCommand command = new SQLiteCommand(strainStatement, connection);
                command.ExecuteNonQuery();

                strainStatement = "insert into dbconfig values('"+databaseIp+"','"+databaseUser + "','" +databasePwd + "','" +databaseName + "','" +databaseTable + "')";
                command = new SQLiteCommand(strainStatement, connection);
                command.ExecuteNonQuery();
                connection.Close();
            }
        }

        private void buttonStart_Click(object sender, EventArgs e)
        {
            buttonStart.Enabled = false;
            buttonStop.Enabled = true;
            foreach(DataStore item in dataStoreCollection)
            {
                item.Start();
            }
            backgroundWorkerSaveData.RunWorkerAsync();
        }

        private void buttonStop_Click(object sender, EventArgs e)
        {
            buttonStart.Enabled = true;
            buttonStop.Enabled = false;
            foreach (DataStore item in dataStoreCollection)
            {
                item.Stop();
            }

            backgroundWorkerSaveData.CancelAsync();
        }

        private void BackgroundWorkerSaveData_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;

            DataTable dt = GetTableSchema();

            while (true)
            {
                try
                {
                    int dataCount = dataQueue.Count;

                    if (dataCount > 100)
                    {
                        if (saveDataSuccess)
                        {
                            dt.Clear();
                        }
                        
                        for (int i = 0; i < dataCount; i++)
                        {
                            DataValue dv;
                            bool success = dataQueue.TryDequeue(out dv);
                            if (success)
                            {
                                DataRow row = dt.NewRow();
                                row[0] = dv.SensorId;
                                row[1] = DateTime.Parse(dv.TimeStamp);
                                row[2] = dv.ValueType;
                                row[3] = dv.Value;
                                dt.Rows.Add(row);
                            }
                        }
                        InsertData(dt, this.databaseTable);
                    }
                }
                catch (Exception ex)
                {
                    using (StreamWriter sw = new StreamWriter(@"ErrLog.txt", true))
                    {
                        sw.WriteLine(ex.Message + " \r\n" + ex.StackTrace.ToString());
                        sw.WriteLine("---------------------------------------------------------");
                        sw.Close();
                    }
                }



                if (bgWorker.CancellationPending == true)
                {
                    e.Cancel = true;
                    break;
                }

                Thread.Sleep(3000);
            }
        }

        private DataTable GetTableSchema()
        {
            DataTable dt = new DataTable();
            dt.Columns.AddRange(new DataColumn[] {
                //new DataColumn("ID",typeof(int)),
                new DataColumn("SensorId",typeof(string)),
                new DataColumn("Stamp",typeof(System.DateTime)),
                new DataColumn("Type",typeof(string)),
                new DataColumn("Value",typeof(Single))
            });
            return dt;
        }

        private void InsertData(DataTable dt, string tableName)
        {
            string connectionString = "Data Source = "+databaseIp+";Network Library = DBMSSOCN;Initial Catalog = "+databaseName+";User ID = "+databaseUser+";Password = "+databasePwd;

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    SqlBulkCopy bulkCopy = new SqlBulkCopy(conn);
                    bulkCopy.DestinationTableName = tableName;
                    bulkCopy.BatchSize = dt.Rows.Count;
                    //bulkCopy.BulkCopyTimeout
                    conn.Open();
                    
                    if (dt != null && dt.Rows.Count != 0)
                    {
                        bulkCopy.WriteToServer(dt);
                    }
                    
                    conn.Close();
                    saveDataSuccess = true;
                }
            }
            catch (Exception ex)
            {
                saveDataSuccess = false;
                if (textBoxLog.InvokeRequired)
                {
                    textBoxLog.BeginInvoke(new MethodInvoker(() =>
                    {
                        textBoxLog.AppendText(ex.Message + "\r\n");
                    }));
                }
                else
                {
                    textBoxLog.AppendText(ex.Message + "\r\n");
                }
            }    
        }

        private void ToolStripMenuDatabaseItem_Click(object sender, EventArgs e)
        {
            DatabaseConfig dlg = new DatabaseConfig(this);
            dlg.ShowDialog();
        }

        private void buttonTest_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrWhiteSpace(textBoxKey.Text)){
                return;
            }
            string key = textBoxKey.Text;
            IDatabase db = this.redis.GetDatabase(0);
            byte[] receiveBytes = db.StringGet(key);
            MessageBox.Show(System.Text.Encoding.Default.GetString(receiveBytes));
        }

        private void textBoxLog_TextChanged(object sender, EventArgs e)
        {
            if (textBoxLog.Text.Length >= textBoxLog.MaxLength)
            {
                textBoxLog.Clear();
            }
        }
    }
    public class DataValue
    {
        public string SensorId { get; set; }
        public string TimeStamp { get; set; }
        public string ValueType { get; set; }
        public double Value { get; set; }
    }
}
