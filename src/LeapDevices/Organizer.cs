using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.IO.MemoryMappedFiles;
using System.Diagnostics;

using VVVV.Core;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VMath;
using VVVV.Utils.VColor;
using VVVV.Utils.SharedMemory;

using Leap;

namespace VVVV.Nodes
{
    [PluginInfo(Name = "FingerList", Category = "Leap", Tags = "")]
    public class LeapFingerListNode : IPluginEvaluate
    {
        public enum FilterFinger
        {
            All,
            Thumb,
            Index,
            Middle,
            Ring,
            Pinky
        };

        [Input("Hands")]
        public Pin<Hand> FHand;
        [Input("Filter")]
        public Pin<FilterFinger> FFilter;
        
        [Output("Fingers")]
        public ISpread<ISpread<Finger>> FAll;

        public void Evaluate(int SpreadMax)
        {
            if (FHand.IsConnected)
            {
                FAll.SliceCount = FHand.SliceCount;

                for (int i = 0; i < FHand.SliceCount; i++)
                {
                    FAll[i].SliceCount = 0;

                    for (int j = 0; j < FFilter.SliceCount; j++)
                    {
                        if(FFilter[j] == FilterFinger.All)
                            foreach (Finger f in FHand[i].Fingers) FAll[i].Add(f);
                        else
                        {
                            int address = (int)FFilter[j] - 1;
                            Finger tmp = FHand[i].Fingers.Where((Finger f) => { return f.Type == (Finger.FingerType)address; }).ToArray()[0];
                            FAll[i].Add(tmp);
                        }
                    }
                }
            }
            else
            {
                FAll.SliceCount = 0;
            }
        }
    }
}
