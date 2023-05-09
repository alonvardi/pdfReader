// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;
using System.Diagnostics;
using static pdfReader.MainWindow;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Newtonsoft.Json;
using static Google.Apis.Requests.BatchRequest;
using System.Drawing;
using ABI.Windows.Foundation;
using static System.Net.Mime.MediaTypeNames;
using System.Drawing.Imaging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace pdfReader
{
    public sealed partial class MainWindow : Window
    {

        private int _currentPageIndex = 0;
        private PdfDocument _pdfDocument;
        private static GoogleOCRResponse googleOCRResponse = null;
        private BitmapImage bitmap;
        public MainWindow()
        {
            this.InitializeComponent();
        }

        [ComImport, Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IInitializeWithWindow
        {
            void Initialize(IntPtr hwnd);
        }

        private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new FileOpenPicker();
            openFileDialog.FileTypeFilter.Add(".pdf");

            // Initialize FileOpenPicker with the window handle
            IntPtr windowHandle = WindowNative.GetWindowHandle(this);
            IInitializeWithWindow initializeWithWindow = openFileDialog.As<IInitializeWithWindow>();
            initializeWithWindow.Initialize(windowHandle);

            StorageFile file = await openFileDialog.PickSingleFileAsync();
            if (file != null)
            {
                // Load the PDF using PdfDocument
                using (IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read))
                {
                    _pdfDocument = await PdfDocument.LoadFromStreamAsync(fileStream);

                }

                // Render the first page of the PDF document
                await RenderPdfPageAsync(0);
            }
        }

        private async Task RenderPdfPageAsync(int pageIndex)
        {
            if (_pdfDocument == null || pageIndex < 0 || pageIndex >= _pdfDocument.PageCount)
            {
                return;
            }

            PdfPage page = _pdfDocument.GetPage((uint)pageIndex);
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                await page.RenderToStreamAsync(stream);
                bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                PdfImage.Source = bitmap;
                byte[] dataImage = await InMemoryRandomAccessStreamToByteArray(stream);
                await ocrWithGoogle(dataImage);

            }
        }
        private async Task<byte[]> InMemoryRandomAccessStreamToByteArray(InMemoryRandomAccessStream stream)
        {
            var dataReader = new DataReader(stream.GetInputStreamAt(0));
            var bytes = new byte[stream.Size];
            await dataReader.LoadAsync((uint)stream.Size);
            dataReader.ReadBytes(bytes);
            return bytes;
        }
        private async void PdfScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (PdfScrollViewer.VerticalOffset == 0 && _currentPageIndex > 0)
            {
                // Scroll to the bottom of the new page
                PdfScrollViewer.ChangeView(null, PdfScrollViewer.ScrollableHeight - 1, null);

                // User scrolled to the top, go to the previous page
                _currentPageIndex--;
                await RenderPdfPageAsync(_currentPageIndex);

            }
            else if (PdfScrollViewer.VerticalOffset == PdfScrollViewer.ScrollableHeight && _currentPageIndex < _pdfDocument.PageCount - 1)
            {

                // Scroll to the top of the new page
                PdfScrollViewer.ChangeView(null, 1, null);
                // User scrolled to the bottom, go to the next page
                _currentPageIndex++;
                await RenderPdfPageAsync(_currentPageIndex);

            }
        }

        private async Task ocrWithGoogle(byte[] bitmapSource)
        {


            string base64Image = Convert.ToBase64String(bitmapSource);
            string apiKey = "AIzaSyCx_BMdOZfxVFeObjDX1erlQDx3V6Z6ido";
            string visionApiUrl = $"https://vision.googleapis.com/v1/images:annotate?key={apiKey}";

            var jsonRequestData = new
            {
                requests = new[] {
          new {
            image = new {
                content = base64Image
              },
              features = new [] {
                new {
                  type = "TEXT_DETECTION"
                }
              }
          }
        }
            };

            string jsonString = System.Text.Json.JsonSerializer.Serialize(jsonRequestData);

            using HttpClient httpClient = new HttpClient();
            HttpResponseMessage response = await httpClient.PostAsync(
              visionApiUrl,
              new StringContent(jsonString, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                try
                {

                    googleOCRResponse = JsonConvert.DeserializeObject<GoogleOCRResponse>(responseContent);
                    googleOCRResponse.Responses[0].TextAnnotations.RemoveAt(0);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Deserialization exception: " + ex.Message);
                }
                if (googleOCRResponse.Responses != null && googleOCRResponse.Responses.Count > 0)
                {
                    foreach (TextAnnotation textAnnotation in googleOCRResponse.Responses[0].TextAnnotations)
                    {
                        Debug.WriteLine(textAnnotation);
                        // Your code to process the textAnnotations
                    }
                }
                //Debug.WriteLine("RESPONSE!!!! "+apiResponse.TextAnnotations[0].Description);
                //Debug.WriteLine("OCR Response: " + responseContent);
            }
            else
            {
                Debug.WriteLine("An error occurred: " + response.ReasonPhrase);
            }

        }

        public static int FindTextInCoordinates(int xClicked, int yClicked)
        {
            int i = 0;
            var result = new List<TextAnnotation>();
            foreach (var textAnnotation in googleOCRResponse.Responses[0].TextAnnotations)
            {
                var xMin = textAnnotation.BoundingPoly.Vertices[0].X;
                var yMin = textAnnotation.BoundingPoly.Vertices[0].Y;
                var xMax = textAnnotation.BoundingPoly.Vertices[2].X;
                var yMax = textAnnotation.BoundingPoly.Vertices[2].Y;

                if (xClicked >= xMin && xClicked <= xMax && yClicked >= yMin && yClicked <= yMax)
                {
                    return i;
                }
                i++;
            }

            return -1;
        }

        private void PdfImage_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var position = e.GetCurrentPoint(PdfImage).Position;
            int x = (int)position.X;
            int y = (int)position.Y;

            int location = FindTextInCoordinates(x, y);


            if (location != -1)
            {
                // Find the text at the clicked coordinates
                TextAnnotation clickedTextAnnotations = googleOCRResponse.Responses[0].TextAnnotations[location];
                Debug.WriteLine($"Clicked text number {location} description {clickedTextAnnotations.Description}, XMin: {clickedTextAnnotations.BoundingPoly.Vertices[0].X}, YMin: {clickedTextAnnotations.BoundingPoly.Vertices[0].Y} , XMAX{clickedTextAnnotations.BoundingPoly.Vertices[2].X} , YMAX{clickedTextAnnotations.BoundingPoly.Vertices[2].Y}");
                Debug.WriteLine($"end sentence location {endSentence(location)}");
                int endOfSentence = endSentence(location);
                if (endOfSentence != -1)
                {
                    markLines(saparateSentenceToLines(location, endOfSentence));
                    
                    SpeakText(retrieveTextFromLocation(location, endOfSentence));
                }
            }
            else
            {
                Debug.WriteLine($"No text found at the clicked coordinates. x:{x} , y: {y}");
            }
        }




        private int endSentence(int wordLocation)
        {
            String point = ".";
            for (int i = wordLocation; i < googleOCRResponse.Responses[0].TextAnnotations.Count; i++)
            {
                if (googleOCRResponse.Responses[0].TextAnnotations[i].Description.Contains(point) || googleOCRResponse.Responses[0].TextAnnotations[i].Description[0] == point[0])
                    return i;
            }
            return -1;
        }

        private List<int[]> saparateSentenceToLines(int wordLocation, int endLocation)
        {
            List<int[]> lines = new List<int[]>();
            int start = wordLocation;
            const int errorSpace = 5;
            int[] arr = new int[2];
            for (int i = wordLocation + 1; i < endLocation; i++)
            {
                int yMinSpace = googleOCRResponse.Responses[0].TextAnnotations[i - 1].BoundingPoly.Vertices[0].Y - googleOCRResponse.Responses[0].TextAnnotations[i].BoundingPoly.Vertices[0].Y;
                int yMaxSpace = googleOCRResponse.Responses[0].TextAnnotations[i - 1].BoundingPoly.Vertices[2].Y - googleOCRResponse.Responses[0].TextAnnotations[i].BoundingPoly.Vertices[2].Y;
                if (!(yMinSpace >= -errorSpace && yMinSpace <= errorSpace) || !(yMaxSpace >= -errorSpace && yMaxSpace <= errorSpace))
                {
                    arr = new int[2];
                    arr[0] = start;
                    arr[1] = i - 1;
                    lines.Add(arr);
                    start = i;
                }
            }
            arr = new int[2];
            arr[0] = start;
            arr[1] = endLocation;
            lines.Add(arr);
            return lines;
        }

        private void markLines(List<int[]> lines)
        {
            DrawingCanvas.Children.Clear();
            foreach (int[] line in lines)
            {
                int x, y, width, height;
                Debug.WriteLine($"line start at {line[0]} and end at {line[1]}");
                x = googleOCRResponse.Responses[0].TextAnnotations[line[0]].BoundingPoly.Vertices[0].X;
                y = googleOCRResponse.Responses[0].TextAnnotations[line[0]].BoundingPoly.Vertices[0].Y;
                width = googleOCRResponse.Responses[0].TextAnnotations[line[1]].BoundingPoly.Vertices[2].X - x;
                height = googleOCRResponse.Responses[0].TextAnnotations[line[1]].BoundingPoly.Vertices[2].Y - y;

                if (width < 0 )
                {
                    x = googleOCRResponse.Responses[0].TextAnnotations[line[1]].BoundingPoly.Vertices[0].X;
                    width = googleOCRResponse.Responses[0].TextAnnotations[line[0]].BoundingPoly.Vertices[2].X - x;
                }


                var rectangle = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 0, 0)),
                    Width = width,
                    Height = height,
                    Margin = new Thickness(x, y, 0, 0)
                };
                DrawingCanvas.Children.Add(rectangle);

            }
        }

        private async void SpeakText(string text)
        {
            using (var speechSynthesizer = new SpeechSynthesizer())
            {
                var speechStream = await speechSynthesizer.SynthesizeTextToStreamAsync(text);
                var mediaPlayer = new MediaPlayer();
                mediaPlayer.Source = MediaSource.CreateFromStream(speechStream, speechStream.ContentType);
                mediaPlayer.Play();
            }
        }

        private String retrieveTextFromLocation(int start,int end)
        {
            String text = null;
            for(int i = start; i < end; i++)
            {
                text += " "+googleOCRResponse.Responses[0].TextAnnotations[i].Description;
            }
            return text;
        }
        /*        private Image DrawRectangleOnImage(Rectangle rect, Color color, int lineWidth)
                {
                    // Create a copy of the original image to draw on
                    Image newImage = (Image)PdfImage.Clone();

                    // Create a Graphics object from the image
                    using (Graphics graphics = Graphics.FromImage(newImage))
                    {
                        // Create a Pen with the specified color and line width
                        using (Pen pen = new Pen(color, lineWidth))
                        {
                            // Draw the rectangle on the image
                            graphics.DrawRectangle(pen, rect);
                        }
                    }

                    return newImage;
                }*/
    }
}