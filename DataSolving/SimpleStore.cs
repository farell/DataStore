using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json;
using System.Windows.Forms;
using System.Threading;

namespace DataSolving
{
    class SimpleStore:DataStore
    {
        private System.Timers.Timer timer;
        private BackgroundWorker backgroundWorker;
        private List<string> list;
        /// <summary>
        /// in second
        /// </summary>
        private int period;
        private int redisDbIndex;

        public SimpleStore(List<string> keys,int period,ConnectionMultiplexer redis, int redisIndex,ConcurrentQueue<DataValue> queue, TextBox log) :base(redis, queue,log)
        {
            this.period = period;
            redisDbIndex = redisIndex;
            list = keys;
            timer = new System.Timers.Timer(period*1000);
            globalStamp = new Dictionary<string, string>();
            timer.Elapsed += Timer_Elapsed;
            InitStamp();
            backgroundWorker = new BackgroundWorker();
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.DoWork += BackgroundWorker_DoWork;
        }

        protected override void InitStamp()
        {
            for(int i = 0; i < list.Count; i++)
            {
                globalStamp.Add(list[i], "");
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
            backgroundWorker.RunWorkerAsync();
        }

        public override void Stop()
        {
            timer.Stop();
            backgroundWorker.CancelAsync();
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            if (redis.IsConnected)
            {

            }
            else
            {
                string content = " redis server is not connected";
                this.AppendLog(content+stamp);
                return;
            }

            try
            {
                IDatabase db = this.redis.GetDatabase(redisDbIndex);

                List<RedisKey> keyCollection = new List<RedisKey>();

                foreach(string key in list)
                {
                    keyCollection.Add(key);
                }

                RedisKey[] keys = keyCollection.ToArray();

                RedisValue[] vals = db.StringGet(keys);

                foreach (RedisValue rv in vals)
                {
                    if (!rv.IsNull)
                    {
                        DataValue dv = JsonConvert.DeserializeObject<DataValue>((string)rv);
                        string key = dv.SensorId + "-" + dv.ValueType;
                        if (globalStamp[key] != dv.TimeStamp)
                        {
                            string str = dv.SensorId + " " + dv.TimeStamp + " " + dv.ValueType + " " + dv.Value;
                            this.dataQueue.Enqueue(dv);
                            this.AppendLog(stamp + " " + str);
                            globalStamp[key] = dv.TimeStamp;
                        }
                        else
                        {
                            this.AppendLog(stamp + " " + key + " not updated");
                        }
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
