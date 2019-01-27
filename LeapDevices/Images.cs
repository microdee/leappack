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
using mp.pddn;

using FeralTic.DX11.Resources;
using FeralTic.DX11;
using VVVV.DX11;

using Leap;
using LeapInternal;

namespace VVVV.Nodes
{
    [PluginInfo(Name = "Images", Category = "Leap")]
    public unsafe class LeapImagesNode : IPluginEvaluate, IDX11ResourceHost, IDisposable
    {
        [Input("Controller")]
        public Pin<Controller> FController;
        [Input("Enabled")]
        public ISpread<bool> FEnabled;

        [Output("Image Left")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FImgTexOutL;
        [Output("Distortion Map Left")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FDistMapL;

        [Output("Image Right")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FImgTexOutR;
        [Output("Distortion Map Right")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FDistMapR;

        [Output("Frame ID")]
        protected ISpread<long> FFrameID;

        private Image ValidImage;
        private byte[] imagedataL = new byte[307200];
        private bool ImageReady = true;
        
        private byte[] imagedataR = new byte[307200];

        private bool FInvalidate;
        private int fcr = 0;

        public void Evaluate(int SpreadMax)
        {
            if (FEnabled[0] && FController.IsConnected && FController.SliceCount > 0 && FController.TryGetSlice(0) != null)
            {
                if(fcr == 0)
                {
                    FImgTexOutL.SliceCount = FDistMapL.SliceCount = FImgTexOutR.SliceCount = FDistMapR.SliceCount = FController.SliceCount;
                    //Connection.GetConnection().
                    FController[0].FrameReady += (sender, args) =>
                    {
                        if (!ImageReady) return;
                        FFrameID[0] = args.frame.Id;
                        ImageReady = false;
                    };
                    FController[0].ImageReady += (sender, args) =>
                    {
                        ValidImage = args.image;
                        imagedataL = args.image.Data(Image.CameraType.LEFT);
                        imagedataR = args.image.Data(Image.CameraType.RIGHT);
                        FInvalidate = true;
                        ImageReady = true;
                        //FImageFailed[0] = false;
                    };
                }
                
                for (int i = 0; i < FImgTexOutL.SliceCount; i++)
                {
                    if (FImgTexOutL[i] == null) { FImgTexOutL[i] = new DX11Resource<DX11DynamicTexture2D>(); }
                    if (FDistMapL[i] == null) { FDistMapL[i] = new DX11Resource<DX11DynamicTexture2D>(); }
                    if (FImgTexOutR[i] == null) { FImgTexOutR[i] = new DX11Resource<DX11DynamicTexture2D>(); }
                    if (FDistMapR[i] == null) { FDistMapR[i] = new DX11Resource<DX11DynamicTexture2D>(); }
                }
                if(FImgTexOutL.SliceCount > FController.SliceCount)
                {
                    for(int i=FController.SliceCount; i<(FImgTexOutL.SliceCount-FController.SliceCount); i++)
                    {
                        FImgTexOutL[i]?.Dispose();
                        FDistMapL[i]?.Dispose();
                        FImgTexOutR[i]?.Dispose();
                        FDistMapR[i]?.Dispose();
                    }
                }
                FImgTexOutL.SliceCount = FDistMapL.SliceCount = FImgTexOutR.SliceCount = FDistMapR.SliceCount = FController.SliceCount;
                fcr++;
            }
            else
            {
                if (FImgTexOutL.SliceCount > 0)
                {
                    for (int i = 0; i < FImgTexOutL.SliceCount; i++)
                    {
                        FImgTexOutL[i]?.Dispose();
                        FDistMapL[i]?.Dispose();
                        FImgTexOutR[i]?.Dispose();
                        FDistMapR[i]?.Dispose();
                    }
                    FImgTexOutL.SliceCount = 0;
                    FDistMapL.SliceCount = 0;
                    FImgTexOutR.SliceCount = 0;
                    FDistMapR.SliceCount = 0;
                }
                fcr = 0;
            }
        }

        public void UpdateImage(DX11RenderContext context, DX11Resource<DX11DynamicTexture2D> texture, byte[] data)
        {
            if (FInvalidate || !texture.Contains(context))
            {

                var fmt = SlimDX.DXGI.Format.R8_UNorm;

                if (texture.Contains(context))
                {
                    var imgdesc = texture[context].Resource.Description;

                    if (imgdesc.Width != ValidImage.Width || imgdesc.Height != ValidImage.Height || imgdesc.Format != fmt)
                    {
                        texture.Dispose(context);
                        texture[context] = new DX11DynamicTexture2D(context, ValidImage.Width, ValidImage.Height, fmt);
                    }
                }
                else
                {
                    texture[context] = new DX11DynamicTexture2D(context, ValidImage.Width, ValidImage.Height, fmt);
#if DEBUG
                    texture[context].Resource.DebugName = "DynamicTexture";
#endif
                }
                texture[context].WriteData(data);
            }
        }

        public void UpdateDistMap(DX11RenderContext context, DX11Resource<DX11DynamicTexture2D> texture, Image.CameraType side)
        {
            if (FInvalidate || !texture.Contains(context))
            {

                var fmt = SlimDX.DXGI.Format.R32G32_Float;

                if (texture.Contains(context))
                {
                    var imgdesc = texture[context].Resource.Description;

                    if (imgdesc.Width != ValidImage.DistortionWidth / 2 || imgdesc.Height != ValidImage.DistortionHeight || imgdesc.Format != fmt)
                    {
                        texture.Dispose(context);
                        texture[context] = new DX11DynamicTexture2D(context, ValidImage.DistortionWidth / 2, ValidImage.DistortionHeight, fmt);
                    }
                }
                else
                {
                    texture[context] = new DX11DynamicTexture2D(context, ValidImage.DistortionWidth / 2, ValidImage.DistortionHeight, fmt);
#if DEBUG
                    texture[context].Resource.DebugName = "DynamicTexture";
#endif
                }

                texture[context].WriteData(ValidImage.Distortion(side), 2);
            }
        }

        public void Update(DX11RenderContext context)
        {
            if(ValidImage == null) return;
            if (FImgTexOutL.SliceCount == 0) { return; }
            for (int i = 0; i < FImgTexOutL.SliceCount; i++)
            {
                UpdateImage(context, FImgTexOutL[i], imagedataL);
                UpdateImage(context, FImgTexOutR[i], imagedataR);
                UpdateDistMap(context, FDistMapL[i], Image.CameraType.LEFT);
                UpdateDistMap(context, FDistMapR[i], Image.CameraType.RIGHT);
            }
            FInvalidate = false;
        }

        public void Destroy(DX11RenderContext context, bool force)
        {

            for (int i = 0; i < FImgTexOutL.SliceCount; i++)
            {
                FImgTexOutL[i]?.Dispose(context);
                FDistMapL[i]?.Dispose(context);
                FImgTexOutR[i]?.Dispose(context);
                FDistMapR[i]?.Dispose(context);
            }
        }


        #region IDisposable Members
        public void Dispose()
        {
            if (FImgTexOutL.SliceCount > 0)
            {
                for (int i = 0; i < FImgTexOutL.SliceCount; i++)
                {
                    FImgTexOutL[i]?.Dispose();
                    FDistMapL[i]?.Dispose();
                    FImgTexOutR[i]?.Dispose();
                    FDistMapR[i]?.Dispose();
                }
            }
        }
        #endregion
    }
}
