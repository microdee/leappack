using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.IO.MemoryMappedFiles;

using VVVV.Core;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VMath;
using VVVV.Utils.VColor;
using VVVV.Utils.SharedMemory;

using SlimDX.Direct3D11;
using SlimDX;

using FeralTic.DX11.Resources;
using FeralTic.DX11;
using VVVV.DX11;

using Leap;

namespace VVVV.Nodes
{
    [PluginInfo(Name = "Images", Category = "Leap")]
    public unsafe class LeapImagesNode : IPluginEvaluate, IDX11ResourceProvider, IDisposable
    {
        [Input("Controller")]
        public Pin<Controller> FController;
        [Input("Enabled")]
        public ISpread<bool> FEnabled;

        [Output("Image")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FLeft;

        [Output("Is Valid")]
        protected ISpread<bool> FValid;

        private long FrameID = -1;
        private Image ValidImage;

        private bool FInvalidate;
        private int fcr = 0;

        public void Evaluate(int SpreadMax)
        {
            if (FController.IsConnected && this.FEnabled[0])
            {
                if(fcr == 0)
                {
                    this.FLeft.SliceCount = FController.SliceCount;
                }
                this.FInvalidate = true;
                if(FrameID == -1)
                {
                    FrameID = FController[0].Frame(0).Id;
                }
                var timg = FController[0].RequestImages(FrameID, Image.ImageType.DEFAULT);
                if(timg.IsComplete)
                {
                    FrameID = FController[0].Frame(0).Id;
                    ValidImage = timg;
                }
                
                for (int i = 0; i < FLeft.SliceCount; i++)
                {
                    if (this.FLeft[i] == null) { this.FLeft[i] = new DX11Resource<DX11DynamicTexture2D>(); }
                }
                if(FLeft.SliceCount > FController.SliceCount)
                {
                    for(int i=FController.SliceCount; i<(FLeft.SliceCount-FController.SliceCount); i++)
                    {
                        if (this.FLeft[i] != null) { this.FLeft[i].Dispose(); }
                    }
                }
                this.FLeft.SliceCount = FController.SliceCount;
                fcr++;
            }
            else
            {
                if (this.FLeft.SliceCount > 0)
                {
                    for (int i = 0; i < FLeft.SliceCount; i++)
                    {
                        if (this.FLeft[i] != null) { this.FLeft[i].Dispose(); }
                    }
                    this.FLeft.SliceCount = 0;
                }
                fcr = 0;
            }

        }

        public void Update(IPluginIO pin, DX11RenderContext context)
        {
            if (FLeft.SliceCount == 0) { return; }
            for (int i = 0; i < FLeft.SliceCount; i++)
            {
                if (this.FInvalidate || !this.FLeft[i].Contains(context))
                {

                    SlimDX.DXGI.Format fmt = SlimDX.DXGI.Format.R8_UNorm;

                    Texture2DDescription LeftDesc;

                    if (this.FLeft[i].Contains(context))
                    {
                        LeftDesc = this.FLeft[i][context].Resource.Description;

                        if (LeftDesc.Width != ValidImage.Width || LeftDesc.Height != ValidImage.Height || LeftDesc.Format != fmt)
                        {
                            this.FLeft[i].Dispose(context);
                            this.FLeft[i][context] = new DX11DynamicTexture2D(context, ValidImage.Width, ValidImage.Height, fmt);
                        }
                    }
                    else
                    {
                        this.FLeft[i][context] = new DX11DynamicTexture2D(context, ValidImage.Width, ValidImage.Height, fmt);
#if DEBUG
                        this.FLeft[i][context].Resource.DebugName = "DynamicTexture";
#endif
                    }

                    LeftDesc = this.FLeft[i][context].Resource.Description;

                    this.FLeft[i][context].WriteData(ValidImage.Data);
                    this.FInvalidate = false;
                }
            }
        }

        public void Destroy(IPluginIO pin, DX11RenderContext context, bool force)
        {

            for (int i = 0; i < FLeft.SliceCount; i++)
            {
                this.FLeft[i].Dispose(context);
            }
        }


        #region IDisposable Members
        public void Dispose()
        {
            if (this.FLeft.SliceCount > 0)
            {
                for (int i = 0; i < FLeft.SliceCount; i++)
                {
                    if (this.FLeft[i] != null)
                    {
                        this.FLeft[i].Dispose();
                    }
                }
            }
        }
        #endregion
    }
}
