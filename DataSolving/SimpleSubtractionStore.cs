using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Timers;
using System.Windows.Forms;

namespace DataSolving
{
    class SimpleSubtractionValue
    {
        //public string SensorId;
        public double InitValue;
        public string ValueType;
        public SimpleSubtractionValue(string valueType ,double init)
        {
            //this.SensorId = sensorId;
            this.ValueType = valueType;
            this.InitValue = init;
        }
    }
    class SimpleSubtractionStore:DataStore
    {
        private System.Timers.Timer timer;
        private BackgroundWorker backgroundWorker;
        private Dictionary<string, SimpleSubtractionValue> list;
        private Dictionary<string, string> stamp;

        public SimpleSubtractionStore(Dictionary<string, SimpleSubtractionValue> keys, int period, ConnectionMultiplexer redis, TextBox log) : base(redis, log)
        {
            list = keys;
            timer = new System.Timers.Timer(period * 1000);
            stamp = new Dictionary<string, string>();
            timer.Elapsed += Timer_Elapsed;
            InitStamp();
            backgroundWorker = new BackgroundWorker();
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.DoWork += BackgroundWorker_DoWork;
        }

        private void InitStamp()
        {
            foreach (string key in list.Keys)
            {
                stamp.Add(key, "");
            }
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!backgroundWorker.IsBusy)
            {
                backgroundWorker.RunWorkerAsync();
            }
        }

        public override void Start()
        {
            timer.Start();
        }

        public override void Stop()
        {
            redis.Close();
            timer.Stop();
            backgroundWorker.CancelAsync();
        }

        private DataValue GetDataValue(IDatabase db, string key)
        {
            string str = "";
            byte[] receiveBytes = db.StringGet(key);
            if (receiveBytes == null)
            {
                str = "key " + key + " not exist\r\n";
                return null;
            }
            else
            {
                DataValue dv = (DataValue)serializer.UnpackSingleObject(receiveBytes);

                if (dv.SensorId == null)
                {
                    str = "maleformed packet";
                    return null;
                }
                else
                {
                    string st = "";
                    try
                    {
                        st = stamp[key];
                        if (st == dv.TimeStamp)
                        {
                            str = "data not updated!\r\n";
                            return null;
                        }
                        else
                        {
                            stamp[key] = dv.TimeStamp;
                            str = dv.SensorId + " " + dv.TimeStamp + " " + dv.ValueType + " " + dv.Value + "\r\n";
                            return dv;
                            //this.dataQueue.Enqueue(dv);
                            //this.logger.AppendText(str);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.AppendLog(key + " " + ex.ToString() + " \r\n" + ex.StackTrace.ToString());
                        return null;
                    }
                }
            }
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;

            try
            {
                IDatabase db = this.redis.GetDatabase(0);

                foreach (string key in list.Keys)
                {
                    SimpleSubtractionValue ssv = list[key];

                    DataValue dv = GetDataValue(db, key);
                    if (dv == null)
                    {
                        this.AppendLog(key + " is null");
                        continue;
                    }

                    dv.Value = dv.Value - ssv.InitValue;
                    dv.ValueType = ssv.ValueType;

                    string offsetKey = dv.SensorId + "-" + ssv.ValueType;
                    

                    byte[] result = serializer.PackSingleObject(dv);
                    db.StringSet(offsetKey, result);

                    this.AppendLog(offsetKey+" : " +dv.Value );

                    if (bgWorker.CancellationPending == true)
                    {
                        e.Cancel = true;
                        break;
                    }
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
        }
    }
}
