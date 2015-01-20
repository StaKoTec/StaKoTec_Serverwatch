using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Management;
using System.Runtime.InteropServices;
using AutomationX;
using System.Diagnostics;
using System.Timers;

namespace StaKoTec_Serverwatch
{
    class App
    {
        AX _aX = null;
        AXInstance _mainInstance = null;
        LogLevel _logLevel = LogLevel.Debug;
        PerformanceCounter _cpuCounter = null;
        Boolean _disposing = false;
        Thread _getSmartDataThread = null;
        static System.Timers.Timer _getSmartDataTimer = null;

        public void Run(String instanceName)
        {
            try
            {
                try
                {
                    _aX = new AX();
                }
                catch (AXException ex)
                {
                    Console.WriteLine(ex.Message + "\r\n" + ex.StackTrace);
                    Dispose();
                }
                _mainInstance = new AXInstance(_aX, instanceName, "Status", "err");
                _aX.ShuttingDown += On_aX_ShuttingDown;
                Logging.Init(_aX, _mainInstance);

                _getSmartDataTimer = new System.Timers.Timer(15 * 60 * 1000);
                _getSmartDataTimer.Enabled = true;
                _getSmartDataTimer.AutoReset = true;
                _getSmartDataTimer.Elapsed += _getSmartDataTimer_Elapsed;
                _getSmartDataTimer_Elapsed(null, null);

                AXVariable lifetick = _mainInstance.Get("Lifetick");
                AXVariable aXcycleCounter = _mainInstance.Get("CycleCounter");
                AXVariable releaseStartCapi = _mainInstance.Get("Start_CAPI_Release");
                Int32 cycleCounter = 0;
                _mainInstance.Get("CAPI_Running").Set(true);


                _cpuCounter = new PerformanceCounter();
                _cpuCounter.CategoryName = "Processor";
                _cpuCounter.CounterName = "% Processor Time";
                _cpuCounter.InstanceName = "_Total";
                _cpuCounter.NextValue();

                while (!_disposing)
                {
                    try
                    {
                        if (!releaseStartCapi.GetBool())
                            Dispose();
                        

                        lifetick.Set(true);
                        aXcycleCounter.Set(cycleCounter);
                        cycleCounter++;

                        if ((cycleCounter % 10) == 0)
                            GetCPULoad();

                        /*if ((cycleCounter % 600) == 0)
                            startGetSmartData = true;

                        if (startGetSmartData && (_getSmartDataThread == null || !_getSmartDataThread.IsAlive || _getSmartDataThread.ThreadState == System.Threading.ThreadState.Aborted))
                        {
                            //Logging.WriteLog(cycleCounter.ToString() + " geht los");
                            _getSmartDataThread = new Thread(GetSmartData);
                            _getSmartDataThread.Start();
                            startGetSmartData = false;
                        }*/


                        Thread.Sleep(100);
                    }
                    catch (Exception ex)
                    {
                        Logging.WriteLog(LogLevel.Error, ex.Message, ex.StackTrace);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.WriteLog(LogLevel.Error, ex.Message, ex.StackTrace);
            }
        }

        void On_aX_ShuttingDown(AX sender)
        {
            Dispose();
        }

        void _getSmartDataTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (_getSmartDataThread == null || !_getSmartDataThread.IsAlive || _getSmartDataThread.ThreadState == System.Threading.ThreadState.Aborted)
                {
                    _getSmartDataThread = new Thread(GetSmartData);
                    _getSmartDataThread.Start();
                }
            }
            catch (Exception ex)
            {
                Logging.WriteLog(LogLevel.Error, ex.Message, ex.StackTrace);
            }
        }

        void Dispose()
        {
            try
            {
                if (_disposing) return;
                _disposing = true;

                Console.WriteLine("Aus, Ende!");
                _cpuCounter.Dispose();
                _getSmartDataTimer.Dispose();
                _getSmartDataThread.Abort();
                _mainInstance.Get("err").Set(false);
                _mainInstance.Get("CAPI_Running").Set(false);
                Logging.WriteLog(LogLevel.Always, "StaKoTec_Serverwatch.exe beendet");
            }
            catch (Exception ex)
            {
                Logging.WriteLog(LogLevel.Error, ex.Message, ex.StackTrace);
            }
            finally
            {
                Environment.Exit(0);
            }
        }

        void GetCPULoad()
        {
            try
            {
                _mainInstance.Get("CPULoad").Set(_cpuCounter.NextValue());
                var totalRam = new Microsoft.VisualBasic.Devices.ComputerInfo();
                float tempreal = 0;
                if (float.TryParse(totalRam.TotalPhysicalMemory.ToString(), out tempreal))
                    _mainInstance.Get("Ram").Set(tempreal / 1024 / 1024 / 1024);
                else
                    _mainInstance.Get("Ram").Set(0);

                if (float.TryParse(totalRam.AvailablePhysicalMemory.ToString(), out tempreal))
                    _mainInstance.Get("FreeRam").Set(tempreal / 1024 / 1024 / 1024);
                else
                    _mainInstance.Get("FreeRam").Set(0);
            }
            catch (Exception ex)
            {
                Logging.WriteLog(LogLevel.Error, ex.Message, ex.StackTrace);
            }
        }

        void GetSmartData()
        {
            try
            {
                // retrieve list of drives on computer (this will return both HDD's and CDROM's and Virtual CDROM's)          
                _mainInstance.Status = DateTime.Now.ToString() + ": Suche nach Festplatten-Daten...";

                var dicDrives = new Dictionary<int, HDD>();

                var wdSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                // extract model and interface information
                int iDriveIndex = 0;
                foreach (ManagementObject drive in wdSearcher.Get())
                {
                    var hdd = new HDD();

                    foreach (PropertyData prop in drive.Properties)
                    {
                        if (prop.Value == null)
                            continue;

                        //Console.WriteLine("{0}: {1}", prop.Name, prop.Value);
                        if (prop.Name == "Model")
                            hdd.Model = prop.Value.ToString().Trim();
                        else if (prop.Name == "InterfaceType")
                            hdd.Type = prop.Value.ToString().Trim();
                        else if (prop.Name == "FirmwareRevision")
                            hdd.Firmware = prop.Value.ToString().Trim();
                    }
                    //Console.WriteLine("--------------");
                    dicDrives.Add(iDriveIndex, hdd);
                    iDriveIndex++;
                }



                var pmsearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMedia");
                // retrieve hdd serial number
                iDriveIndex = 0;
                foreach (ManagementObject drive in pmsearcher.Get())
                {
                    // because all physical media will be returned we need to exit
                    // after the hard drives serial info is extracted
                    if (iDriveIndex >= dicDrives.Count)
                        break;

                    dicDrives[iDriveIndex].Serial = drive["SerialNumber"] == null ? "None" : drive["SerialNumber"].ToString().Trim();
                    iDriveIndex++;
                }



                var ldsearcher = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk");
                // retrieve hdd serial number
                iDriveIndex = 0;
                foreach (ManagementObject drive in ldsearcher.Get())
                {
                    // because all physical media will be returned we need to exit
                    // after the hard drives serial info is extracted
                    if (iDriveIndex >= dicDrives.Count)
                        break;

                    Int64 tempInt = 0;
                    foreach (PropertyData prop in drive.Properties)
                    {
                        //Console.WriteLine("{0}: {1}", prop.Name, prop.Value);
                        if (prop.Value == null)
                            continue;

                        if (prop.Name == "Size")
                        {
                            if (Int64.TryParse(prop.Value.ToString().Trim(), out tempInt))
                                dicDrives[iDriveIndex].Size = (tempInt / 1024 / 1024 / 1024);
                            else
                                dicDrives[iDriveIndex].Size = 0;
                        }
                        else if (prop.Name == "FreeSpace")
                        {
                            if (Int64.TryParse(prop.Value.ToString().Trim(), out tempInt))
                                dicDrives[iDriveIndex].FreeSpace = (tempInt / 1024 / 1024 / 1024);
                            else
                                dicDrives[iDriveIndex].FreeSpace = 0;
                        }
                        else if (prop.Name == "Name")
                            dicDrives[iDriveIndex].DriveLetter = prop.Value.ToString().Trim();
                        else if (prop.Name == "Description")
                            dicDrives[iDriveIndex].Description = prop.Value.ToString().Trim();

                    }

                    iDriveIndex++;
                }

                // get wmi access to hdd 
                var searcher = new ManagementObjectSearcher("Select * from Win32_DiskDrive");
                searcher.Scope = new ManagementScope(@"\root\wmi");

                // check if SMART reports the drive is failing
                searcher.Query = new ObjectQuery("Select * from MSStorageDriver_FailurePredictStatus");
                iDriveIndex = 0;
                foreach (ManagementObject drive in searcher.Get())
                {
                    dicDrives[iDriveIndex].IsOK = (bool)drive.Properties["PredictFailure"].Value == false;
                    iDriveIndex++;
                }

                // retrive attribute flags, value worste and vendor data information
                searcher.Query = new ObjectQuery("Select * from MSStorageDriver_FailurePredictData");
                iDriveIndex = 0;
                foreach (ManagementObject data in searcher.Get())
                {
                    Byte[] bytes = (Byte[])data.Properties["VendorSpecific"].Value;
                    for (int i = 0; i < 30; ++i)
                    {
                        try
                        {
                            int id = bytes[i * 12 + 2];

                            int flags = bytes[i * 12 + 4]; // least significant status byte, +3 most significant byte, but not used so ignored.
                            //bool advisory = (flags & 0x1) == 0x0;
                            bool failureImminent = (flags & 0x1) == 0x1;
                            //bool onlineDataCollection = (flags & 0x2) == 0x2;

                            int value = bytes[i * 12 + 5];
                            int worst = bytes[i * 12 + 6];
                            int vendordata = BitConverter.ToInt32(bytes, i * 12 + 7);
                            if (id == 0) continue;

                            var attr = dicDrives[iDriveIndex].Attributes[id];
                            attr.Current = value;
                            attr.Worst = worst;
                            attr.Data = vendordata;
                            attr.IsOK = failureImminent == false;
                        }
                        catch
                        {
                            // given key does not exist in attribute collection (attribute not in the dictionary of attributes)
                        }
                    }
                    iDriveIndex++;
                }

                // retreive threshold values foreach attribute
                searcher.Query = new ObjectQuery("Select * from MSStorageDriver_FailurePredictThresholds");
                iDriveIndex = 0;
                foreach (ManagementObject data in searcher.Get())
                {
                    Byte[] bytes = (Byte[])data.Properties["VendorSpecific"].Value;
                    for (int i = 0; i < 30; ++i)
                    {
                        try
                        {

                            int id = bytes[i * 12 + 2];
                            int thresh = bytes[i * 12 + 3];
                            if (id == 0) continue;

                            var attr = dicDrives[iDriveIndex].Attributes[id];
                            attr.Threshold = thresh;
                        }
                        catch
                        {
                            // given key does not exist in attribute collection (attribute not in the dictionary of attributes)
                        }
                    }

                    iDriveIndex++;
                }


                // print
                UInt16 x = 0;
                //String driveLetter = _mainInstance.Get("DriveLetterString").GetString() + ":";
                _mainInstance.Get("State").Set(x, "UNKNOWN");
                _mainInstance.Get("LastRequest").Set(DateTime.Now.ToString());
                foreach (var drive in dicDrives)
                {
                    if (x > _mainInstance.Get("HardDriveLetter").Length)
                        break;
                    if (drive.Value.Type == "USB")
                        continue;

                    _mainInstance.Get("HardDriveLetter").Set(x, drive.Value.DriveLetter);
                    _mainInstance.Get("Description").Set(x, drive.Value.Description);
                    _mainInstance.Get("Serialnumber").Set(x, drive.Value.Serial);
                    _mainInstance.Get("Model").Set(x, drive.Value.Model);
                    _mainInstance.Get("Firmware").Set(x, drive.Value.Firmware);
                    _mainInstance.Get("Type").Set(x, drive.Value.Type);
                    _mainInstance.Get("Size").Set(x, (float)drive.Value.Size);
                    _mainInstance.Get("FreeSpace").Set(x, (float)drive.Value.FreeSpace);
                    _mainInstance.Get("State").Set(x, (drive.Value.IsOK) ? "OK" : "BAD");

                    _mainInstance.Get("HDD_Status").Set(x, (drive.Value.IsOK) ? 1 : 2);

                    if (!drive.Value.IsOK)
                        _mainInstance.Error = "Fehler in Festplatte! " + drive.Value.DriveLetter;

                    x++;
                    /*
                    try
                    {
                        foreach (var attr in drive.Value.Attributes)
                        {
                            if (!attr.Value.HasData)
                                continue;

                            if (x > _mainInstance.Get("Data_Name").Length)
                                break;

                            _mainInstance.Get("Data_Name").Set(x, attr.Value.Attribute);
                            _mainInstance.Get("Data_Current").Set(x, attr.Value.Current);
                            _mainInstance.Get("Data_Worst").Set(x, attr.Value.Worst);
                            _mainInstance.Get("Data_Threshold").Set(x, attr.Value.Threshold);
                            _mainInstance.Get("Data_StatusINT").Set(x, (attr.Value.IsOK) ? 1 : 2);
                            _mainInstance.Get("Data_Status").Set(x, (attr.Value.IsOK) ? "OK" : "BAD");

                            x++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _mainInstance.Error = "C-API Fehler: " + ex.Message;
                    }

                    break;*/
                }
                /*
                for (; x < _mainInstance.Get("Data_Name").Length; x++)
                {
                    _mainInstance.Get("Data_Name").Set(x, "");
                    _mainInstance.Get("Data_Current").Set(x, 0);
                    _mainInstance.Get("Data_Worst").Set(x, 0);
                    _mainInstance.Get("Data_Threshold").Set(x, 0);
                    _mainInstance.Get("Data_StatusINT").Set(x, 0);
                    _mainInstance.Get("Data_Status").Set(x, "");
                }
                */
                for (; x < _mainInstance.Get("HardDriveLetter").Length; x++)
                {
                    _mainInstance.Get("HardDriveLetter").Set(x, "");
                    _mainInstance.Get("Description").Set(x, "");
                    _mainInstance.Get("Serialnumber").Set(x, "");
                    _mainInstance.Get("Model").Set(x, "");
                    _mainInstance.Get("Firmware").Set(x, "");
                    _mainInstance.Get("Type").Set(x, "");
                    _mainInstance.Get("Size").Set(x, (float)0);
                    _mainInstance.Get("FreeSpace").Set(x, (float)0);
                    _mainInstance.Get("State").Set(x, "");
                    _mainInstance.Get("HDD_Status").Set(x, 0);
                }
                _mainInstance.Status = DateTime.Now.ToString() + ": Fertig";
            }
            catch (ThreadAbortException)
            {
                Logging.WriteLog(LogLevel.Info, "GetSmartData Thread abgebrochen.");
            }
            catch (Exception ex)
            {
                Logging.WriteLog(LogLevel.Error, ex.Message, ex.StackTrace);
            }
        }

    }
}
