﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.MemoryMappedFiles;
using SlimDX;

using VVVV.Core;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VMath;
using VVVV.Utils.VColor;
using VVVV.Utils.SharedMemory;

using Leap;

namespace VVVV.Nodes
{
    public static class TrackingHelper
    {
        public static Matrix4x4 ToMatrix4x4(this Leap.LeapTransform m)
        {
            Matrix4x4 tmp = new Matrix4x4();
            tmp.m11 = m.xBasis.x;
            tmp.m12 = m.yBasis.x;
            tmp.m13 = m.zBasis.x;
            tmp.m21 = m.xBasis.y;
            tmp.m22 = m.yBasis.y;
            tmp.m23 = m.zBasis.y;
            tmp.m31 = m.xBasis.z;
            tmp.m32 = m.yBasis.z;
            tmp.m33 = m.zBasis.z;
            tmp.m44 = 1;
            return tmp;
        }
        public static Vector4D ToVector4D(this Leap.LeapQuaternion V)
        {
            Vector4D tmp = new Vector4D((double)V.x, (double)V.y, (double)V.z, (double)V.w);
            return tmp;
        }
        public static Vector3D ToVector3D(this Leap.Vector V)
        {
            Vector3D tmp = new Vector3D((double)V.x, (double)V.y, (double)V.z);
            return tmp;
        }
        public static Vector ToLeapVector(this Vector3D V)
        {
            Vector tmp = new Vector((float)V.x, (float)V.y, (float)V.z);
            return tmp;
        }
        public static Vector3D mul(this Vector3D V, Matrix4x4 m)
        {
            Vector4 tmpv = SlimDX.Vector3.Transform(V.ToSlimDXVector(), m.ToSlimDXMatrix());
            Vector3D tmp = new Vector3D(tmpv.X, tmpv.Y, tmpv.Z);
            return tmp;
        }
        public static Vector3D mulz(this Vector3D V, double m)
        {
            V.z *= m;
            return V;
        }
        public static Matrix4x4 mulz(this Matrix4x4 m, double zm)
        {
            m.col3 *= zm;
            return m;
        }
    }

    [PluginInfo(Name = "Device", Category = "Leap", Tags = "", AutoEvaluate=true)]
    public class LeapDeviceNode : IPluginEvaluate
    {
        [Input("Scale")]
        public Pin<float> FScale;
        [Input("Mirror Z", DefaultValue = 1.0)]
        public Pin<bool> FMirror;

        [Input("Reinitialize", IsBang=true)]
        public ISpread<bool> FReinit;

        [Input("Age Filtering Threshold", Visibility = PinVisibility.OnlyInspector, DefaultValue=1500)]
        public Pin<float> FAgeThreshold;

        [Input("Background Frames")]
        public ISpread<bool> FBckgFrames;
        [Input("Optimize for HMD")]
        public ISpread<bool> FHMD;
        
        [Output("Device")]
        public ISpread<Leap.Device> FDevice;

        [Output("Controller")]
        public ISpread<Leap.Controller> FController;
        [Output("Last Frame")]
        public ISpread<Frame> FFrame;

        public static Leap.Device leapdevice;
        public static Leap.Controller leapcontroller;
        public bool FirstFrame = true;

        public static float GlobalScale = (float)0.01;
        public static float AgeCorrectionThreshold = (float)1500;
        public static double GlobalZMul = 1;

        private void leapinit()
        {
            leapcontroller = new Controller();
            Controller.PolicyFlag flags = Controller.PolicyFlag.POLICY_DEFAULT | (Controller.PolicyFlag)(1 << 1);
            if (FBckgFrames[0]) flags = flags | Controller.PolicyFlag.POLICY_BACKGROUND_FRAMES;
            if (FHMD[0]) flags = flags | Controller.PolicyFlag.POLICY_OPTIMIZE_HMD;
            leapcontroller.SetPolicy(flags);
            try
            {
                leapdevice = leapcontroller.Devices[0];
            }
            catch (Exception e) { }
        }

        public void OnImportsSatisfied() { }

        public void Evaluate(int SpreadMax)
        {
            if(leapcontroller!=null)
            {
                FDevice.SliceCount = 1;
                FController.SliceCount = 1;
                FFrame.SliceCount = 1;

                FDevice[0] = leapdevice;
                FController[0] = leapcontroller;
                FFrame[0] = leapcontroller.Frame(0);
                leapdevice = leapcontroller.Devices[0];
            }
            else
            {
                FDevice.SliceCount = 0;
                FController.SliceCount = 0;
                FFrame.SliceCount = 0;
            }
            if (FReinit[0] || FirstFrame)
            {
                if (leapcontroller != null) leapcontroller.Dispose();
                leapinit();
            }
            GlobalScale = FScale[0];
            GlobalZMul = (FMirror[0]) ? -1 : 1;
            AgeCorrectionThreshold = FAgeThreshold[0];
            FirstFrame = false;
        }
    }

    [PluginInfo(Name = "DeviceInfo", Category = "Leap", Tags = "")]
    public class LeapDeviceInfoNode : IPluginEvaluate
    {
        [Input("Device")]
        public Pin<Leap.Device> FDevice;
        [Input("Boundary Reference Position")]
        public ISpread<Vector3D> FBoundPos;

        [Output("View Angles")]
        public ISpread<Vector2D> FViewAngle;
        [Output("Range")]
        public ISpread<float> FRange;
        [Output("Streaming")]
        public ISpread<bool> FStreaming;

        public void Evaluate(int SpreadMax)
        {

            if (!FDevice.IsConnected || FDevice.SliceCount == 0)
            {
                FViewAngle.SliceCount = 0;
                FRange.SliceCount = 0;
                FStreaming.SliceCount = 0;
            }
            else
            {
                float gs;
                double zm;
                try {
                    gs = VVVV.Nodes.LeapDeviceNode.GlobalScale;
                    zm = VVVV.Nodes.LeapDeviceNode.GlobalZMul;
                }
                catch {
                    gs = 1;
                    zm = 1;
                }

                FViewAngle.SliceCount = 1;
                FRange.SliceCount = 1;
                FStreaming.SliceCount = 1;

                FViewAngle[0] = new Vector2D(FDevice[0].HorizontalViewAngle, FDevice[0].VerticalViewAngle);
                FViewAngle[0] /= 2 * Math.PI;
                FRange[0] = FDevice[0].Range * gs;
                FStreaming[0] = FDevice[0].IsStreaming;
            }
        }
    }

    [PluginInfo(Name = "InteractionBox", Category = "Leap", Tags = "")]
    public class LeapInteractionBoxNode : IPluginEvaluate
    {
        [Input("Frame")]
        public Pin<InteractionBox> FInteractionBox;
        [Input("World Position")]
        public ISpread<ISpread<Vector3D>> FWorldPos;

        [Output("Dimensions")]
        public ISpread<Vector3D> FDimensions;
        [Output("Center")]
        public ISpread<Vector3D> FCenter;
        [Output("Normalized Position")]
        public ISpread<ISpread<Vector3D>> FNormPos;

        public void Evaluate(int SpreadMax)
        {

            if (!FInteractionBox.IsConnected || FInteractionBox.SliceCount == 0)
            {
                FDimensions.SliceCount = 0;
                FNormPos.SliceCount = 0;
                FCenter.SliceCount = 0;
            }
            else
            {
                float gs;
                double zm;
                try
                {
                    gs = VVVV.Nodes.LeapDeviceNode.GlobalScale;
                    zm = VVVV.Nodes.LeapDeviceNode.GlobalZMul;
                }
                catch
                {
                    gs = 1;
                    zm = 1;
                }

                FDimensions.SliceCount = FInteractionBox.SliceCount;
                FCenter.SliceCount = FInteractionBox.SliceCount;
                FNormPos.SliceCount = FInteractionBox.SliceCount;

                for (int i = 0; i < FInteractionBox.SliceCount; i++)
                {
                    FDimensions[i] = new Vector3D(
                        FInteractionBox[i].Width,
                        FInteractionBox[i].Height,
                        FInteractionBox[i].Depth);
                    FDimensions[i] = FDimensions[i] * gs;

                    FCenter[i] = FInteractionBox[i].Center.ToVector3D().mulz(zm) * gs;

                    FNormPos[i].SliceCount = FWorldPos[i].SliceCount;
                    for (int j = 0; j < FWorldPos.SliceCount; j++)
                    {
                        Vector3D tpos = FWorldPos[i][j].mulz(zm) / gs;
                        Leap.Vector V = tpos.ToLeapVector();
                        FNormPos[i][j] = FInteractionBox[i].NormalizePoint(V).ToVector3D().mulz(zm);
                    }
                }
            }
        }
    }
}
