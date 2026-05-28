using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace haulages_bot.Services
{
    public class LogMessage
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; }
        public bool IsError { get; set; }
    }

    public class LogHistoryService
    {
        private readonly ConcurrentDictionary<int, List<LogMessage>> _logs = new ConcurrentDictionary<int, List<LogMessage>>();

        public void AddLog(int serverId, string message, bool isError = false)
        {
            var list = _logs.GetOrAdd(serverId, _ => new List<LogMessage>());
            lock (list)
            {
                list.Add(new LogMessage { Message = message, IsError = isError });
                if (list.Count > 5000)
                {
                    list.RemoveAt(0);
                }
            }
        }

        public List<LogMessage> GetLogs(int serverId)
        {
            if (_logs.TryGetValue(serverId, out var list))
            {
                lock (list)
                {
                    return list.ToList();
                }
            }
            return new List<LogMessage>();
        }

        public void ClearLogs(int serverId)
        {
            if (_logs.TryGetValue(serverId, out var list))
            {
                lock (list)
                {
                    list.Clear();
                }
            }
        }
    }
}
