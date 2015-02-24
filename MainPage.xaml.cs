﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Globalization;
using Windows.UI.Popups;
using Windows.Media.MediaProperties;
using Windows.Media.Devices;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;
using Lumia.Imaging.Transforms;
using Lumia.Imaging;
using MathNet.Numerics.LinearAlgebra.Factorization;
using System.Diagnostics;
using MathNet.Numerics.LinearAlgebra;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=391641

namespace HDR
{
	/// <summary>
	/// An empty page that can be used on its own or navigated to within a Frame.
	/// </summary>
	public sealed partial class MainPage : Page
	{
		private MediaCapture mediaCapture;
        private ImageEncodingProperties imageEncoding;
        private DeviceInformation deviceInformation;
        private MessageDialog messageDialog;
        private FocusSettings focusSettings;
        private double firstExposureTime, secondExposureTime, thirdExposureTime;
        private int firstImageHeight, firstImageWidth, secondImageHeight, secondImageWidth, thirdImageHeight, thirdImageWidth;
        private bool autoISO = false;

		public MainPage()
		{
			this.InitializeComponent();

			this.NavigationCacheMode = NavigationCacheMode.Required;
		}

		/// <summary>
		/// Invoked when this page is about to be displayed in a Frame.
		/// </summary>
		/// <param name="e">Event data that describes how this page was reached.
		/// This parameter is typically used to configure the page.</param>
		protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			// TODO: Prepare page for display here.

			// TODO: If your application contains multiple pages, ensure that you are
			// handling the hardware Back button by registering for the
			// Windows.Phone.UI.Input.HardwareButtons.BackPressed event.
			// If you are using the NavigationHelper provided by some templates,
            // this event is handled for you.

            InitializeCamera();
		}
        
        private async void InitializeCamera()
        {
            mediaCapture = new MediaCapture();
            deviceInformation = await GetCameraDeviceInfoAsync(Windows.Devices.Enumeration.Panel.Back);

            var settings = new MediaCaptureInitializationSettings();
            settings.PhotoCaptureSource = PhotoCaptureSource.VideoPreview;
            if (deviceInformation != null)
                settings.VideoDeviceId = deviceInformation.Id;

            await mediaCapture.InitializeAsync(settings);
            mediaCapture.VideoDeviceController.PrimaryUse = CaptureUse.Photo;

            imageEncoding = ImageEncodingProperties.CreateJpeg();

            focusSettings = new FocusSettings();
            focusSettings.AutoFocusRange = AutoFocusRange.FullRange;
            focusSettings.Mode = FocusMode.Auto;
            focusSettings.WaitForFocus = true;
            focusSettings.DisableDriverFallback = false;
            mediaCapture.VideoDeviceController.FocusControl.Configure(focusSettings);

            mediaCapture.VideoDeviceController.FlashControl.Enabled = false;

            mediaCapture.SetPreviewRotation(VideoRotation.Clockwise90Degrees);

            previewElement.Source = mediaCapture;
            await mediaCapture.StartPreviewAsync();
        }

        private static async Task<DeviceInformation> GetCameraDeviceInfoAsync(Windows.Devices.Enumeration.Panel desiredPanel)
        {
            DeviceInformation device = (await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture))
                .FirstOrDefault(d => d.EnclosureLocation != null && d.EnclosureLocation.Panel == desiredPanel);

            if (device == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "No suitable devices found for the camera of type {0}.", desiredPanel));
            }

            return device;
        }

        private void HDR_High_Click(object sender, RoutedEventArgs e)
        {
            CaptureImage((float)2);
        }

        private void HDR_Medium_Click(object sender, RoutedEventArgs e)
        {
            CaptureImage((float)1.5);
        }

        private void HDR_Low_Click(object sender, RoutedEventArgs e)
        {
            CaptureImage((float)1);
        }

        private void ISO_Click(object sender, RoutedEventArgs e)
        {
            Button b = (Button)sender;
            if (!autoISO)
                b.Content = "Auto ISO";
            else
                b.Content = "Min ISO";

            autoISO = !autoISO;
        }

		private async void CaptureImage(float EV)
        {
            List<IImageProvider> images = new List<IImageProvider>();
            await mediaCapture.VideoDeviceController.FocusControl.FocusAsync();
            if(!autoISO)
                await mediaCapture.VideoDeviceController.IsoSpeedControl.SetValueAsync(mediaCapture.VideoDeviceController.IsoSpeedControl.Min);
            else
                await mediaCapture.VideoDeviceController.IsoSpeedControl.SetAutoAsync();

            await mediaCapture.VideoDeviceController.ExposureCompensationControl.SetValueAsync(-EV);
            var saveImageResults = await SaveImage();
            images.Add(saveImageResults.Item1);
            firstExposureTime = saveImageResults.Item2;
            firstImageHeight = saveImageResults.Item3;
            firstImageWidth = saveImageResults.Item4;

            await mediaCapture.VideoDeviceController.ExposureCompensationControl.SetValueAsync((float)0);
            saveImageResults = await SaveImage();
            images.Add(saveImageResults.Item1);
            secondExposureTime = saveImageResults.Item2;
            secondImageHeight = saveImageResults.Item3;
            secondImageWidth = saveImageResults.Item4;

            await mediaCapture.VideoDeviceController.ExposureCompensationControl.SetValueAsync(EV);
            saveImageResults = await SaveImage();
            images.Add(saveImageResults.Item1);
            thirdExposureTime = saveImageResults.Item2;
            thirdImageHeight = saveImageResults.Item3;
            thirdImageWidth = saveImageResults.Item4;

            saveImageResults = null;

            await mediaCapture.VideoDeviceController.ExposureCompensationControl.SetValueAsync(0);

            List<IImageProvider> alignedImages = await AlignImages(images);
            images = null;

            List<byte[]> pixelImages = await GetPixels(alignedImages);
            alignedImages = null;

            firstExposureTime = 0.25;
            secondExposureTime = 0.5;
            thirdExposureTime = 0.75;
            PerformHDR(pixelImages);
		}

        private async Task<Tuple<StreamImageSource, double, int, int>> SaveImage()
        {
            var photoStorageFile = await KnownFolders.CameraRoll.CreateFileAsync("photo.jpg", CreationCollisionOption.GenerateUniqueName);
            var fileStream = await photoStorageFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
            var imageStream = new InMemoryRandomAccessStream();
            await mediaCapture.CapturePhotoToStreamAsync(imageEncoding, imageStream);

            imageStream.Seek(0);
            var rotateDecoder = await BitmapDecoder.CreateAsync(imageStream);

            var memStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateForTranscodingAsync(memStream, rotateDecoder);

            encoder.BitmapTransform.Rotation = BitmapRotation.Clockwise90Degrees;

            try
            {
                await encoder.FlushAsync();
            }
            catch (Exception err)
            {
                switch (err.HResult)
                {
                    case unchecked((int)0x88982F81): //WINCODEC_ERR_UNSUPPORTEDOPERATION
                        // If the encoder does not support writing a thumbnail, then try again
                        // but disable thumbnail generation.
                        encoder.IsThumbnailGenerated = false;
                        break;
                    default:
                        throw err;
                }
            }

            memStream.Seek(0);
            fileStream.Seek(0);
            fileStream.Size = 0;
            await RandomAccessStream.CopyAsync(memStream, fileStream);

            imageStream.Dispose();
            memStream.Dispose();

            fileStream.Seek(0);
            var stream = new InMemoryRandomAccessStream();
            stream.Seek(0);
            await RandomAccessStream.CopyAsync(fileStream, stream);

            fileStream.Seek(0);
            ExifLib.JpegInfo info = ExifLib.ExifReader.ReadJpeg(fileStream.AsStream(), photoStorageFile.Name);
            double exposureTime = info.ExposureTime;
            int height = info.Height;
            int width = info.Width;
            fileStream.Dispose();
            
            stream.Seek(0);
            return Tuple.Create(new StreamImageSource(stream.AsStream()), exposureTime, height, width);
        }

        private async Task<List<IImageProvider>> AlignImages(List<IImageProvider> unalignedImages)
        {
            List<IImageProvider> alignedImages = new List<IImageProvider>();
            InvalidOperationException ioe = null;
            int count = 0;
            bool failed = false;

            try
            {
                using (var aligner = new ImageAligner())
                {
                    aligner.Sources = unalignedImages;
                    aligner.ReferenceSource = unalignedImages[0];

                    var alignedSources = await aligner.AlignAsync();

                    foreach (var alignedSource in alignedSources)
                    {
                        if (alignedSource != null)
                        {
                            alignedImages.Add(alignedSource);
                            count++;
                        }
                    }

                    if (count  < 3)
                    {
                        messageDialog = new MessageDialog("Image alignment failed. Your HDR might not be well-aligned.");
                        await messageDialog.ShowAsync();
                        failed = true;
                    }
                    else
                    {
                        messageDialog = new MessageDialog("Image alignment complete");
                        await messageDialog.ShowAsync();
                    }
                }
            }
            catch (InvalidOperationException e)
            {
                ioe = e;
            }
            if (ioe != null)
            {
                messageDialog = new MessageDialog("Image alignment failed. Your HDR might not be well-aligned.");
                await messageDialog.ShowAsync();
                failed = true;
            }

            failed = true;
            if (failed)
            {
                alignedImages = null;
                return unalignedImages;
            }
            else
            {
                unalignedImages = null;
                return alignedImages;
            }
        }

        private async void PrintPixels(List<byte[]> alignedSources)
        {
            foreach (var alignedSource in alignedSources)
            {
                if (alignedSource != null)
                {
                    var photoStorageFile = await KnownFolders.CameraRoll.CreateFileAsync("photo.jpg", CreationCollisionOption.GenerateUniqueName);

                    using (var fileStream = await photoStorageFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
                    {
                        await fileStream.WriteAsync(alignedSource.AsBuffer());
                        await fileStream.FlushAsync();
                    }
                }
            }
        }

        private async Task<List<byte[]>> GetPixels(List<IImageProvider> alignedSources)
        {
            List<byte[]> pixelImages = new List<byte[]>();

            foreach (var alignedSource in alignedSources)
            {
                if (alignedSource != null)
                {
                    using (var renderer = new BitmapRenderer())
                    {
                        renderer.Source = alignedSource;
                        Bitmap alignedBuffer = await renderer.RenderAsync();

                        pixelImages.Add(alignedBuffer.Buffers[0].Buffer.ToArray());
                    }
                }
            }

            return pixelImages;
        }

        private async void PerformHDR(List<byte[]> images)
        {
            List<int[]> samples = Sample(images[1]);

            Matrix<double> A = Matrix<double>.Build.Dense(samples[0].Length * images.Count + 256 + 1, 256 + samples[0].Length, 0);
            Vector<double> b = Vector<double>.Build.Dense(A.RowCount, 0);
            double lambda = 10;
            double weight = 0;
            int[] sample = samples[0];
            byte[] image = null;
            UInt16 value = 0;
            int k = 0;

            for (int i = 0; i < sample.Length; i++)
            {
                for (int j = 0; j < images.Count; j++)
                {
                    image = images[j];
                    value = Convert.ToUInt16(image[sample[i] * 4]);

                    weight = GetWeight(value);
                    A[k, value] = weight;
                    A[k, 256 + i] = -weight;
                    b[k] = weight * GetExposureTime(j);
                    k++;
                }
            }

            A[k, 128] = 1;
            k++;

            for (int i = 1; i <= 254; i++)
            {
                weight = GetWeight(Convert.ToUInt16(i));
                A[k, i - 1] = lambda * weight;
                A[k, i] = -2 * lambda * weight;
                A[k, i + 1] = lambda * weight;
                k++;
            }

            Stopwatch time = new Stopwatch();
            time.Start();
            Svd<double> svd = A.Svd(true);
            time.Stop();

            messageDialog = new MessageDialog(time.Elapsed.TotalSeconds.ToString());
            await messageDialog.ShowAsync();

            Vector<double> x = svd.Solve(b);
            Vector<double> g = x.SubVector(0, 256);
            Vector<double> lE = x.SubVector(256, x.Count - 256);
        }

        private List<int[]> Sample(byte[] image)
        {
            List<int[]> samples = new List<int[]>();
            int[] blueSamples = new int[128];
            int[] greenSamples = new int[128];
            int[] redSamples = new int[128];

            int range = image.Length / 4;
            Random rand = new Random();
            int sample = 0;

            for (int i = 0; i < 128; i++)
            {
                sample = rand.Next(range);

                while (Convert.ToUInt16(image[sample * 4]) < 5 || Convert.ToUInt16(image[sample * 4]) > 250)
                {
                    sample = rand.Next(range); 
                }

                blueSamples[i] = sample;
            }

            for (int i = 0; i < 128; i++)
            {
                sample = rand.Next(range);

                while (Convert.ToUInt16(image[(sample * 4) + 1]) < 5 || Convert.ToUInt16(image[(sample * 4) + 1]) > 250)
                {
                    sample = rand.Next(range);
                }

                greenSamples[i] = sample;
            }

            for (int i = 0; i < 128; i++)
            {
                sample = rand.Next(range);

                while (Convert.ToUInt16(image[(sample * 4) + 2]) < 5 || Convert.ToUInt16(image[(sample * 4) + 2]) > 250)
                {
                    sample = rand.Next(range);
                }

                redSamples[i] = sample;
            }

            samples.Add(blueSamples);
            samples.Add(greenSamples);
            samples.Add(redSamples);

            return samples;
        }

        private double GetWeight(UInt16 value)
        {
           return ((Convert.ToDouble(value) <= 127) ? Convert.ToDouble(value) : (255 - Convert.ToDouble(value)));
        }

        private double GetExposureTime(int image)
        {
            switch(image)
            {
                case 0:
                    return (Math.Log(firstExposureTime));
                case 1:
                    return (Math.Log(secondExposureTime));
                case 2:
                    return (Math.Log(thirdExposureTime));
                default:
                    return 0;
            }
        }
	}
}
