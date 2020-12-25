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
    public class PressureTransmitterValue
    {
        public string SensorId;
        public double InitValue;
        public string RefPoint1;
        public string RefPoint2;
        public double Value;
        public bool IsUpdated;
        public string Stamp;
        public PressureTransmitterValue(string sensorId,double initValue,string ref1,string ref2)
        {
            this.SensorId = sensorId;
            this.InitValue = initValue;
            this.RefPoint1 = ref1;
            this.RefPoint2 = ref2;
            this.Value = 0;
            this.IsUpdated = false;
            this.Stamp = "";
        }
    }

    class PressureTransmitterStore : DataStore
    {
        private System.Timers.Timer timer;
        private BackgroundWorker backgroundWorker;
        private Dictionary<string,PressureTransmitterValue> list;
        private Dictionary<string, string> stamp;
        public PressureTransmitterStore(Dictionary<string, PressureTransmitterValue> keys, int period, ConnectionMultiplexer redis, TextBox log) : base(redis, log)
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

        protected void InitStamp()
        {
            foreach(string key in list.Keys)
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

        private DataValue GetDataValue(IDatabase db,string key)
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
                    string st="";
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
                    } catch (Exception ex)
                    {
                        this.AppendLog(key+" "+ ex.ToString() + " \r\n" + ex.StackTrace.ToString());
                        return null;
                    }
                }
            }
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;
            //string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            try
            {
                IDatabase db = this.redis.GetDatabase(0);

                foreach(string key in list.Keys)
                {
                    DataValue dv = GetDataValue(db, key);
                    if(dv == null)
                    {
                        this.AppendLog(key+" is null");
                        continue;
                    }

                    PressureTransmitterValue ptv = list[key];
                    ptv.IsUpdated = true;
                    ptv.Value = dv.Value;
                    ptv.Stamp = dv.TimeStamp;
                }

                foreach (string key in list.Keys)
                {
                    PressureTransmitterValue ptv = list[key];
                    if(ptv.RefPoint1 == "null")
                    {
                        if (ptv.IsUpdated == false)
                        {
                            this.AppendLog(key + "ref point is not updated");
                            break;
                        }
                        continue;
                    }

                    if (ptv.IsUpdated)
                    {
                        PressureTransmitterValue ref1 = list[ptv.RefPoint1];
                        PressureTransmitterValue ref2 = list[ptv.RefPoint2];
                        if(!ref1.IsUpdated || !ref2.IsUpdated)
                        {
                            this.AppendLog(ptv.RefPoint1 + "ref point is not updated");
                            break;
                        }
                        
                        string offsetKey = ptv.SensorId + "-007";
                        string deflectionKey = ptv.SensorId + "-010";

                        double offset = CalculateOffset(ref1.Value, ref2.Value, ptv.Value);
                        double deflection = CalculateDeflection(offset, ptv.InitValue);

                        DataValue temp = new DataValue();
                        temp.SensorId = ptv.SensorId;
                        temp.TimeStamp = ptv.Stamp;
                        temp.ValueType = "007";
                        temp.Value = offset;
                        byte[] result = serializer.PackSingleObject(temp);
                        db.StringSet(offsetKey, result);

                        temp.ValueType = "010";
                        temp.Value = deflection;
                        result = serializer.PackSingleObject(temp);
                        db.StringSet(deflectionKey, result);

                        if (bgWorker.CancellationPending == true)
                        {
                            e.Cancel = true;
                            break;
                        }
                        string str = temp.SensorId + " " + temp.TimeStamp + " " + temp.ValueType + " " + temp.Value + "\r\n";
                        this.AppendLog(str);
                    }
                }
            }
            catch (Exception ex)
            {
                this.AppendLog(ex.Message);
                using (StreamWriter sw = new StreamWriter(@"ErrLog.txt", true))
                {
                    sw.WriteLine(ex.Message + " \r\n" + ex.StackTrace.ToString());
                    sw.WriteLine("---------------------------------------------------------");
                    sw.Close();
                }
            }
        }

        //private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        //{
        //    BackgroundWorker bgWorker = sender as BackgroundWorker;

        //    try
        //    {
        //        IDatabase db = this.redis.GetDatabase(0);

        //        //string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        //        string refPoint1 = "5600001710001-008";
        //        string refPoint2 = "5600001710008-008";

        //        DataValue point1;
        //        DataValue point2;

        //        this.AppendLog("worker start");

        //        point1 = GetDataValue(db, refPoint1);

        //        if(point1 == null)
        //        {
        //            this.AppendLog("ref point 1 is null");
        //            return;
        //        }

        //        point2 = GetDataValue(db, refPoint2);

        //        if (point2 == null)
        //        {
        //            this.AppendLog("ref point 2 is null");
        //            return;
        //        }

        //        for (int i = 0; i < list.Count; i++)
        //        {
        //            string key = list[i];

        //            if(key == refPoint1 || key == refPoint2)
        //            {
        //                continue;
        //            }

        //            DataValue dv = GetDataValue(db, key);
        //            if(dv == null)
        //            {
        //                continue;
        //            }

        //            string offsetKey = dv.SensorId + "-007";
        //            string deflectionKey = dv.SensorId + "-010";

        //            double offset = CalculateOffset(point1.Value, point2.Value, dv.Value);
        //            double deflection = CalculateDeflection(offset, 0);

        //            DataValue temp = new DataValue();
        //            temp.SensorId = dv.SensorId;
        //            temp.TimeStamp = dv.TimeStamp;
        //            temp.ValueType = "007";
        //            temp.Value = offset;
        //            byte[] result = serializer.PackSingleObject(temp);
        //            db.StringSet(offsetKey, result);

        //            temp.ValueType = "010";
        //            temp.Value = deflection;
        //            result = serializer.PackSingleObject(temp);
        //            db.StringSet(deflectionKey, result);

        //            if (bgWorker.CancellationPending == true)
        //            {
        //                e.Cancel = true;
        //                break;
        //            }
        //            string str = dv.SensorId + " " + dv.TimeStamp + " " + temp.ValueType + " " + temp.Value + "\r\n";
        //            this.AppendLog(str);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        this.AppendLog(ex.Message);
        //        using (StreamWriter sw = new StreamWriter(@"ErrLog.txt", true))
        //        {
        //            sw.WriteLine(ex.Message + " \r\n" + ex.StackTrace.ToString());
        //            sw.WriteLine("---------------------------------------------------------");
        //            sw.Close();
        //        }
        //    }

        //    //if (bgWorker.CancellationPending == true)
        //    //{
        //    //    e.Cancel = true;
        //    //    break;
        //    //}
        //}

        /// <summary>
        /// 返回Pi相对于参考点P2的位移
        /// </summary>
        /// <param name="p1">参考点P1</param>
        /// <param name="p2">参考点P2</param>
        /// <param name="pi">测点Pi</param>
        /// <returns></returns>
        private double CalculateOffset(double p1, double p2, double pi)
        {
            //unit mm
            double L = 1393.308;

            double offset = L * (pi - p2) / (p2 - p1);

            return offset;
        }

        /// <summary>
        /// 返回Pi点的挠度（相对于自身按照时刻的位移）
        /// </summary>
        /// <param name="initValue">安装时刻初始值</param>
        /// <param name="pi">测点Pi</param>
        /// <returns></returns>
        private double CalculateDeflection(double pi,double initValue)
        {
            //unit mm

            double deflection = pi - initValue;

            return deflection;
        }
    }
}
