using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskManagerTelegramBot_Chernykh.Classes
{
    public class Events
    {
        public int DbId { get; set; }
        public DateTime Time { get; set; }
        public string Message { get; set; }
        public Events(DateTime time, string message)
        {
            Time = time;
            Message = message;
            DbId = -1;
        }
    }
}
