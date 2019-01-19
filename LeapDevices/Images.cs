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
    public unsafe class LeapImagesNode : IPluginEvaluate, IDX11ResourceHost, IDisposable
    {
        [Input("Controller")]
        public Pin<Controller> FController;
        [Input("Enabled")]
        public ISpread<bool> FEnabled;

        [Output("Image")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FImgTexOut;
        [Output("Distortion Map")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FDistMap;
        
        [Output("Frame ID")]
        protected ISpread<long> FFrameID;
        [Output("Image Failed")]
        protected ISpread<bool> FImageFailed;
        [Output("Image Failure")]
        protected ISpread<ImageRequestFailedEventArgs> FImgFail;

        private Image ValidImage;
        private byte[] imagedata = new byte[307200];

        private bool FInvalidate;
        private bool ImageReady = true;
        private int fcr = 0;

        public void Evaluate(int SpreadMax)
        {
            if (FController.IsConnected && FEnabled[0])
            {
                if(fcr == 0)
                {
                    FImgTexOut.SliceCount = FController.SliceCount;
                    FDistMap.SliceCount = FController.SliceCount;
                    FController[0].FrameReady += (sender, args) =>
                    {
                        if (!ImageReady) return;
                        FFrameID[0] = args.frame.Id;
                        FController[0].RequestImages(args.frame.Id, Image.ImageType.DEFAULT, imagedata);
                        ImageReady = false;
                    };
                    FController[0].ImageReady += (sender, args) =>
                    {
                        ValidImage = args.image;
                        FInvalidate = true;
                        ImageReady = true;
                        FImageFailed[0] = false;
                    };
                    FController[0].ImageRequestFailed += (sender, args) =>
                    {
                        FImageFailed[0] = true;
                        FImgFail[0] = args;
                        ImageReady = true;
                    };
                }
                
                for (int i = 0; i < FImgTexOut.SliceCount; i++)
                {
                    if (FImgTexOut[i] == null) { FImgTexOut[i] = new DX11Resource<DX11DynamicTexture2D>(); }
                    if (FDistMap[i] == null) { FDistMap[i] = new DX11Resource<DX11DynamicTexture2D>(); }
                }
                if(FImgTexOut.SliceCount > FController.SliceCount)
                {
                    for(int i=FController.SliceCount; i<(FImgTexOut.SliceCount-FController.SliceCount); i++)
                    {
                        if (FImgTexOut[i] != null) { FImgTexOut[i].Dispose(); }
                        if (FDistMap[i] != null) { FDistMap[i].Dispose(); }
                    }
                }
                FImgTexOut.SliceCount = FController.SliceCount;
                FDistMap.SliceCount = FController.SliceCount;
                fcr++;
            }
            else
            {
                if (FImgTexOut.SliceCount > 0)
                {
                    for (int i = 0; i < FImgTexOut.SliceCount; i++)
                    {
                        if (FImgTexOut[i] != null) { FImgTexOut[i].Dispose(); }
                        if (FDistMap[i] != null) { FDistMap[i].Dispose(); }
                    }
                    FImgTexOut.SliceCount = 0;
                    FDistMap.SliceCount = 0;
                }
                fcr = 0;
            }
        }

        public void Update(DX11RenderContext context)
        {
            if(ValidImage == null) return;
            //if (FImgTexOut.SliceCount == 0) { return; }
            for (int i = 0; i < FImgTexOut.SliceCount; i++)
            {
                if (FInvalidate || !FImgTexOut[i].Contains(context))
                {

                    var fmt = SlimDX.DXGI.Format.R8_UNorm;

                    if (FImgTexOut[i].Contains(context))
                    {
                        var imgdesc = FImgTexOut[i][context].Resource.Description;

                        if (imgdesc.Width != ValidImage.Width || imgdesc.Height != ValidImage.Height*2 || imgdesc.Format != fmt)
                        {
                            FImgTexOut[i].Dispose(context);
                            FImgTexOut[i][context] = new DX11DynamicTexture2D(context, ValidImage.Width, ValidImage.Height*2, fmt);
                        }
                    }
                    else
                    {
                        FImgTexOut[i][context] = new DX11DynamicTexture2D(context, ValidImage.Width, ValidImage.Height*2, fmt);
#if DEBUG
                        FImgTexOut[i][context].Resource.DebugName = "DynamicTexture";
#endif
                    }
                    FImgTexOut[i][context].WriteData(imagedata);
                }
                if (FInvalidate || !FDistMap[i].Contains(context))
                {

                    var fmt = SlimDX.DXGI.Format.R32G32_Float;

                    if (FDistMap[i].Contains(context))
                    {
                        var imgdesc = FDistMap[i][context].Resource.Description;

                        if (imgdesc.Width != ValidImage.DistortionWidth/2 || imgdesc.Height != ValidImage.DistortionHeight || imgdesc.Format != fmt)
                        {
                            FDistMap[i].Dispose(context);
                            FDistMap[i][context] = new DX11DynamicTexture2D(context, ValidImage.DistortionWidth/2, ValidImage.DistortionHeight, fmt);
                        }
                    }
                    else
                    {
                        FDistMap[i][context] = new DX11DynamicTexture2D(context, ValidImage.DistortionWidth/2, ValidImage.DistortionHeight, fmt);
#if DEBUG
                        FDistMap[i][context].Resource.DebugName = "DynamicTexture";
#endif
                    }

                    FDistMap[i][context].WriteData(ValidImage.Distortion, 2);
                }
            }
            FInvalidate = false;
        }

        public void Destroy(DX11RenderContext context, bool force)
        {

            for (int i = 0; i < FImgTexOut.SliceCount; i++)
            {
                FImgTexOut[i].Dispose(context);
                FDistMap[i].Dispose(context);
            }
        }


        #region IDisposable Members
        public void Dispose()
        {
            if (FImgTexOut.SliceCount > 0)
            {
                for (int i = 0; i < FImgTexOut.SliceCount; i++)
                {
                    if (FImgTexOut[i] != null)
                    {
                        FImgTexOut[i].Dispose();
                    }
                    if (FDistMap[i] != null)
                    {
                        FDistMap[i].Dispose();
                    }
                }
            }
        }
        #endregion
    }
}
