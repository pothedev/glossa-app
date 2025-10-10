using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Glossa.src.utility
{
    public class GoogleGenderModel
    {
        public bool available { get; set; }
        public string model { get; set; } // will be null if unavailable
    }

    public class GoogleLanguageItem
    {
        public string language { get; set; }
        public string region { get; set; }
        public GoogleGenderModel male { get; set; }
        public GoogleGenderModel female { get; set; }
    }

}
