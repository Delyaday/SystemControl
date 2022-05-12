using System;
using System.Collections.Generic;
using System.Text;
using SystemControl.Utils;

namespace SystemControl.Models
{
    public class CensoredWord : NotificationObject
    {
        private int _count;

        public CensoredWord(string word, int count = 0)
        {
            Word = word;
            Count = count;
        }

        public string Word { get; }

        public int Count
        {
            get { return _count; }
            set
            {
                _count = value;
                NotifyPropertyChanged();
            }
        }
    }
}
