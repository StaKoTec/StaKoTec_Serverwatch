/*
Copyright (c) 2013, Llewellyn Kruger
All rights reserved.
Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS “AS IS” AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE. 

http://www.know24.net/blog/C+WMI+HDD+SMART+Information.aspx 
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using System.Runtime.InteropServices;
using AutomationX;
using System.Diagnostics;

namespace StaKoTec_SMART
{
    /// <summary>
    /// Tested against Crystal Disk Info 5.3.1 and HD Tune Pro 3.5 on 15 Feb 2013.
    /// Findings; I do not trust the individual smart register "OK" status reported back frm the drives.
    /// I have tested faulty drives and they return an OK status on nearly all applications except HD Tune. 
    /// After further research I see HD Tune is checking specific attribute values against their thresholds
    /// and and making a determination of their own (which is good) for whether the disk is in good condition or not.
    /// I recommend whoever uses this code to do the same. For example -->
    /// "Reallocated sector count" - the general threshold is 36, but even if 1 sector is reallocated I want to know about it and it should be flagged.   
    /// </summary>
    public class Program
    {

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Too less arguments! Example: StaKoTec_SMART.exe Instancename");
                Environment.Exit(-1);
            }

            string instance = args[0];
            AX _aX = null;
            AXInstance _mainInstance = null;
            PerformanceCounter cpuCounter = new PerformanceCounter();
            cpuCounter.CategoryName = "Processor";
            cpuCounter.CounterName = "% Processor Time";
            cpuCounter.InstanceName = "_Total";
            cpuCounter.NextValue();

            try
            {
                _aX = new AX();
                _mainInstance = new AXInstance(_aX, instance, "Status", "err");
            }
            catch (AXException ex)
            {
                Console.WriteLine(ex.Message + "\r\n" + ex.StackTrace);
                Environment.Exit(0);
            }

            try
            {
                // retrieve list of drives on computer (this will return both HDD's and CDROM's and Virtual CDROM's)          
                _mainInstance.Status = DateTime.Now.ToString() + ": Suche nach Festplatten...";

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

                _mainInstance.Get("CPULoad").Set(cpuCounter.NextValue());
                var totalRam = new Microsoft.VisualBasic.Devices.ComputerInfo();
                float tempreal = 0;
                if (float.TryParse(totalRam.TotalPhysicalMemory.ToString(), out tempreal))
                    _mainInstance.Get("Ram").Set(tempreal / 1024 / 1024);
                else
                    _mainInstance.Get("Ram").Set(0);

                if (float.TryParse(totalRam.AvailablePhysicalMemory.ToString(), out tempreal))
                    _mainInstance.Get("FreeRam").Set(tempreal / 1024 / 1024);
                else
                    _mainInstance.Get("FreeRam").Set(0);

                // print
                String driveLetter = _mainInstance.Get("DriveLetterString").GetString() + ":";
                _mainInstance.Get("State").Set("UNKNOWN");
                _mainInstance.Get("LastRequest").Set(DateTime.Now.ToString());
                UInt16 x = 0;
                Boolean driveVorhanden = false;
                foreach (var drive in dicDrives)
                {
                    if (drive.Value.DriveLetter != driveLetter)
                        continue;

                    driveVorhanden = true;
                    _mainInstance.Get("HardDriveLetter").Set(driveLetter);
                    _mainInstance.Get("Description").Set(drive.Value.Description);
                    _mainInstance.Get("Serialnumber").Set(drive.Value.Serial);
                    _mainInstance.Get("Model").Set(drive.Value.Model);
                    _mainInstance.Get("Firmware").Set(drive.Value.Firmware);
                    _mainInstance.Get("Type").Set(drive.Value.Type);
                    _mainInstance.Get("Size").Set(drive.Value.Size);
                    _mainInstance.Get("FreeSpace").Set(drive.Value.FreeSpace);
                    _mainInstance.Get("State").Set((drive.Value.IsOK) ? "OK" : "BAD");
                    if (!drive.Value.IsOK)
                        _mainInstance.Error = "Fehler in Festplatte!";
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
                    
                    break;
                }
                for(;x<_mainInstance.Get("Data_Name").Length;x++)
                {
                    _mainInstance.Get("Data_Name").Set(x, "");
                    _mainInstance.Get("Data_Current").Set(x, 0);
                    _mainInstance.Get("Data_Worst").Set(x, 0);
                    _mainInstance.Get("Data_Threshold").Set(x, 0);
                    _mainInstance.Get("Data_StatusINT").Set(x, 0);
                    _mainInstance.Get("Data_Status").Set(x, "");
                }
                if (!driveVorhanden)
                    _mainInstance.Error = "Laufwerk " + driveLetter + " nicht gefunden!";
                _mainInstance.Status = DateTime.Now.ToString() + ": Fertig";
                _mainInstance.Get("Laeuft").Set(false);
            }
            catch (ManagementException e)
            {
                Console.WriteLine("An error occurred while querying for WMI data: " + e.Message);
            }
        }
    }
    
}
