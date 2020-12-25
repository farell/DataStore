using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StackExchange.Redis;
using System.Data.SQLite;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DataSolving
{
    class DataStore
    {
        protected ConcurrentQueue<DataValue> dataQueue;
        protected ConnectionMultiplexer redis;
        protected Dictionary<string, string> globalStamp;
        private TextBox logger;
        public DataStore(ConnectionMultiplexer redis, ConcurrentQueue<DataValue> queue,TextBox log)
        {
            this.dataQueue = queue;
            this.redis = redis;
            this.logger = log;
        }
        public virtual void Start() { }
        public virtual void Stop() { }

        protected virtual void InitStamp(){}

        public void AppendLog(string content)
        {
            if (logger.InvokeRequired)
            {
                logger.BeginInvoke(new MethodInvoker(() =>
                {
                    logger.AppendText(content+"\r\n");
                }));
            }
            else
            {
                logger.AppendText(content + "\r\n");
            }
        }
    }
}
