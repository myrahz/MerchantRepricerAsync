using System.Collections.Generic;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;

using System.Windows.Forms;
namespace MerchantRepricerAsync
{
    public class MerchantRepricerAsyncSettings : ISettings
    {


        [Menu("Hotkey repricing")]
        public HotkeyNode Hotkey { get; set; }

        public ToggleNode DebugMode { get; set; }
        public ToggleNode Simulate { get; set; }
        public ToggleNode RepriceOtherThanChaos { get; set; }
        public RangeNode<int> RepricePercent { get; set; }
        public RangeNode<int> RepriceSubtract { get; set; }
        public RangeNode<int> InputDelay { get; set; }
        public RangeNode<int> ItemDelay { get; set; }

        public HotkeyNode PauseKey { get; set; }
        public ToggleNode Enable { get; set; } = new ToggleNode(false);

        public MerchantRepricerAsyncSettings()
        {

            Hotkey = Keys.F5;
            DebugMode = new ToggleNode(false);
            Simulate = new ToggleNode(false);
            RepriceOtherThanChaos = new ToggleNode(false);
            RepricePercent = new RangeNode<int>(90,55, 99);
            RepriceSubtract = new RangeNode<int>(1,1 , 5);
            InputDelay = new RangeNode<int>(100, 50, 2000);
            ItemDelay = new RangeNode<int>(1000, 500, 5000);
            PauseKey = Keys.Pause;
            
            

        }
    }

}