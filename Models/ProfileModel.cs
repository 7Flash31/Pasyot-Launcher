using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pasyot_Launcher.Models
{
    internal class ProfileModel
    {
        public string Version { get; set; } = string.Empty;
        public string PlayerNickname { get; set; } = "Player";
        public string JavaArgs { get; set; } = string.Empty;
        public string EnvVars { get; set; } = string.Empty;
        public string ModLoader { get; set; } = string.Empty;
    }
}
