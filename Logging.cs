﻿using AutomationX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StaKoTec_Serverwatch
{
    enum LogLevel
    {
        Error = 0,
        Warning = 1,
        Info = 2,
        Debug = 3,
        Always = 100
    }

    static class Logging
    {
        private static AX _aX = null;
        private static AXInstance _mainInstance = null;
        private static System.IO.StreamWriter _logWriter = null;

        public static void Init(AX ax, AXInstance mainInstance)
        {
            _aX = ax;
            _mainInstance = mainInstance;
            String DatumString = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString("D2") + DateTime.Now.Day.ToString("D2") + DateTime.Now.Hour.ToString("D2") + DateTime.Now.Minute.ToString("D2") + DateTime.Now.Second.ToString("D2");
            String logPath = _mainInstance.Get("$$_working_part").GetString() + "\\Log_" + mainInstance.Name + "_" + DatumString + ".txt";
            _logWriter = new System.IO.StreamWriter(logPath, true, Encoding.UTF8, 1024);
            _logWriter.AutoFlush = true;
            WriteLog(LogLevel.Always, "StaKoTec_Serverwatch V 0.1.3 started");
        }

        public static void WriteLog(LogLevel logLevel, String message, String stackTrace = "", Boolean setError = false)
        {
            try
            {
                if (_mainInstance.VariableExists("LogLevel") && (logLevel > (LogLevel)_mainInstance.Get("LogLevel").GetLongInteger()) && !(logLevel == LogLevel.Always))
                    return;

                _logWriter.WriteLine(DateTime.Now.ToString() + "." + DateTime.Now.Millisecond.ToString("D3") + ": " + message);

                _aX.WriteJournal(0, _mainInstance.Name, message, "ON", _mainInstance.Name);
                Console.WriteLine(message);
                if (stackTrace.Length > 0)
                {
                    _aX.WriteJournal(0, _mainInstance.Name, stackTrace, "ON", _mainInstance.Name);
                    Console.WriteLine(stackTrace);
                    _logWriter.WriteLine(DateTime.Now.ToString() + "." + DateTime.Now.Millisecond.ToString("D3") + ": " + stackTrace);
                }
                if (setError)
                    _mainInstance.Error = message;

                _mainInstance.Status = message;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message + "\r\n" + ex.StackTrace);
            }
        }
    }
}
