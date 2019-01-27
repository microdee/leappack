using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

using Leap;

namespace LeapInternal
{
    public partial class Connection
    {
        private ObjectPool<ImageData> _imageDataCache;
        private ObjectPool<ImageData> _imageRawDataCache;

        private ulong _standardImageBufferSize = 640 * 240 * 2; //width * heigth * 2
        private ulong _standardRawBufferSize = 640 * 240 * 2 * 8; //width * heigth * 2 images * 8 bpp
        private DistortionData _currentDistortionData = new DistortionData();

        public Image RequestImages(Int64 frameId, Image.ImageType imageType)
        {
            ImageData imageData;
            int bufferSize = 0;
            if (imageType == Image.ImageType.DEFAULT)
            {
                imageData = _imageDataCache.CheckOut();
                imageData.type = eLeapImageType.eLeapImageType_Default;
                bufferSize = (int)_standardImageBufferSize;
            }
            else
            {
                imageData = _imageRawDataCache.CheckOut();
                imageData.type = eLeapImageType.eLeapImageType_Raw;
                bufferSize = (int)_standardRawBufferSize;
            }
            if (imageData.pixelBuffer == null || imageData.pixelBuffer.Length != bufferSize)
            {
                imageData.pixelBuffer = new byte[bufferSize];
            }
            imageData.frame_id = frameId;
            return RequestImages(imageData);
        }

        public Image RequestImages(Int64 frameId, Image.ImageType imageType, byte[] buffer)
        {
            ImageData imageData = new ImageData();
            if (imageType == Image.ImageType.DEFAULT)
                imageData.type = eLeapImageType.eLeapImageType_Default;
            else
                imageData.type = eLeapImageType.eLeapImageType_Raw;

            imageData.frame_id = frameId;
            imageData.pixelBuffer = buffer;
            return RequestImages(imageData);
        }

        private Image RequestImages(ImageData imageData)
        {
            if (!_isRunning)
                return Image.Invalid;

            LEAP_IMAGE_FRAME_DESCRIPTION imageSpecifier = new LEAP_IMAGE_FRAME_DESCRIPTION();
            imageSpecifier.frame_id = imageData.frame_id;
            imageSpecifier.type = imageData.type;
            imageSpecifier.pBuffer = imageData.getPinnedHandle();
            imageSpecifier.buffer_len = (ulong)imageData.pixelBuffer.LongLength;
            LEAP_IMAGE_FRAME_REQUEST_TOKEN token;
            eLeapRS result = eLeapRS.eLeapRS_UnknownError;

            result = LeapC.RequestImages(_leapConnection, ref imageSpecifier, out token);

            if (result == eLeapRS.eLeapRS_Success)
            {
                imageData.isComplete = false;
                imageData.index = token.requestID;
                Image futureImage = new Image(imageData);
                _pendingImageRequestList.Add(new ImageFuture(futureImage, imageData, LeapC.GetNow(), token));
                return futureImage;
            }
            else
            {
                imageData.unPinHandle();
                reportAbnormalResults("LeapC Image Request call was ", result);
                return Image.Invalid;
            }
        }

        private void handleImageCompletion(ref LEAP_IMAGE_COMPLETE_EVENT imageMsg)
        {
            LEAP_IMAGE_PROPERTIES props;
            StructMarshal<LEAP_IMAGE_PROPERTIES>.PtrToStruct(imageMsg.properties, out props);
            ImageFuture pendingImage = _pendingImageRequestList.FindAndRemove(imageMsg.token);

            if (pendingImage == null)
                return;

            //Update distortion data, if changed
            if ((_currentDistortionData.Version != imageMsg.matrix_version) || !_currentDistortionData.IsValid)
            {
                _currentDistortionData = new DistortionData();
                _currentDistortionData.Version = imageMsg.matrix_version;
                _currentDistortionData.Width = LeapC.DistortionSize; //fixed value for now
                _currentDistortionData.Height = LeapC.DistortionSize; //fixed value for now
                if (_currentDistortionData.Data == null || _currentDistortionData.Data.Length != (2 * _currentDistortionData.Width * _currentDistortionData.Height * 2))
                    _currentDistortionData.Data = new float[(int)(2 * _currentDistortionData.Width * _currentDistortionData.Height * 2)]; //2 float values per map point
                LEAP_DISTORTION_MATRIX matrix;
                StructMarshal<LEAP_DISTORTION_MATRIX>.PtrToStruct(imageMsg.distortionMatrix, out matrix);
                Array.Copy(matrix.matrix_data, _currentDistortionData.Data, matrix.matrix_data.Length);

                if (LeapDistortionChange != null)
                {
                    LeapDistortionChange.DispatchOnContext<DistortionEventArgs>(this, EventContext, new DistortionEventArgs(_currentDistortionData));
                }
            }

            pendingImage.imageData.CompleteImageData(props.type,
                props.format,
                props.bpp,
                props.width,
                props.height,
                imageMsg.info.timestamp,
                imageMsg.info.frame_id,
                props.x_offset,
                props.y_offset,
                props.x_scale,
                props.y_scale,
                _currentDistortionData,
                LeapC.DistortionSize,
                imageMsg.matrix_version);

            Image completedImage = pendingImage.imageObject;

            if (LeapImageReady != null)
            {
                LeapImageReady.DispatchOnContext<ImageEventArgs>(this, EventContext, new ImageEventArgs(completedImage));
            }
        }
    }
}
