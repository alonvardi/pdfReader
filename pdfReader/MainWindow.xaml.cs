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
using Google.Cloud.Vision.V1;
using static System.Net.Mime.MediaTypeNames;
using Google.Cloud.TextToSpeech.V1;
using System.Text;

namespace pdfReader
{
    public sealed partial class MainWindow : Window
    {

        private int _currentPageIndex = 0;
        private PdfDocument _pdfDocument;
        private List<Word> processedWords;
        public MainWindow()
        {
            string credentialsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "key.json");

            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);

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
                BitmapImage bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);

                // Reset stream position to the beginning
                stream.Seek(0);

                // Read the stream into a byte array
                byte[] imageBytes;
                using (var streamReader = stream.AsStream())
                {
                    imageBytes = new byte[streamReader.Length];
                    await streamReader.ReadAsync(imageBytes, 0, imageBytes.Length);
                }

                PdfImage.Source = bitmap;

                await ocrWithGoogle(imageBytes);
            }
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

        private async Task ocrWithGoogle(byte[] imageBytes)
        {
            // Instantiates a client
            var client = ImageAnnotatorClient.Create();

            // Create a Google.Cloud.Vision.V1.Image from the byte array
            var image = Google.Cloud.Vision.V1.Image.FromBytes(imageBytes);

            // Performs text detection on the image file
            var ocrResponse = await client.DetectDocumentTextAsync(image);
            if (ocrResponse != null && ocrResponse.Text != null)
            {

                 processedWords = ProcessOcrResponse(ocrResponse);

                // Now you can query 'processedWords' to find words at specific coordinates, etc.

            }
            else
            {
                Debug.WriteLine("No text found.");
            }
        }

        private List<Word> ProcessOcrResponse(TextAnnotation response)
        {
            List<Word> words = new List<Word>();

            foreach (var page in response.Pages)
            {
                foreach (var block in page.Blocks)
                {
                    foreach (var paragraph in block.Paragraphs)
                    {
                        foreach (var word in paragraph.Words)
                        {
                            var boundingBox = new Rectangle(word.BoundingBox.Vertices[0].X, word.BoundingBox.Vertices[0].Y, word.BoundingBox.Vertices[2].X, word.BoundingBox.Vertices[2].Y);
                            Word newWord = new Word(string.Join("", word.Symbols.Select(s => s.Text)), boundingBox);
                            words.Add(newWord);

                            Debug.WriteLine(newWord.toString());
                        }
                    }
                }
            }

            return words;
        }



        private async void PdfImage_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var position = e.GetCurrentPoint(PdfImage).Position;
            int x = (int)position.X;
            int y = (int)position.Y;

            Debug.WriteLine($"clicked on ({x},{y})");

            var word = FindWordAtCoordinates(x, y);
            if (word != null)
            {
                // speech
                var segments = SegmentText(word.getText());
                await SpeakText(segments);

                Debug.WriteLine($"{word.getText()}");
            }
        }
        private Word FindWordAtCoordinates(int x, int y)
        {
            if (processedWords == null)
            {
                Debug.WriteLine("ProcessedWords NULL!");
                return null;
            }

            return processedWords.FirstOrDefault(word => IsPointInBoundingBox(word.getBoundingBox(), x, y));
        }


        private bool IsPointInBoundingBox(Rectangle boundingBox, int x, int y)
        {
            return x >= boundingBox.getX1() && x <= boundingBox.getX2() && y >= boundingBox.getY1() && y <= boundingBox.getY2();
        }
        static async Task SpeakText(List<(string Text, string LanguageCode)> segments)
        {
            foreach (var segment in segments)
            {
                await SynthesizeAndPlaySegment(segment.Text, segment.LanguageCode);
            }
        }

        static async Task SynthesizeAndPlaySegment(string text, string languageCode)
        {
            TextToSpeechClient client = TextToSpeechClient.Create();
            var input = new SynthesisInput { Text = text };
            var voice = new VoiceSelectionParams
            {
                LanguageCode = languageCode,
                SsmlGender = SsmlVoiceGender.Neutral
            };
            var config = new AudioConfig { AudioEncoding = AudioEncoding.Linear16 };
            var response = await client.SynthesizeSpeechAsync(input, voice, config);

            using (var stream = new MemoryStream(response.AudioContent.ToByteArray()))
            {
                System.Media.SoundPlayer player = new System.Media.SoundPlayer(stream);
                player.PlaySync(); // Play each segment synchronously
            }
        }

        public static List<(string Text, string LanguageCode)> SegmentText(string mixedText)
        {
            var segments = new List<(string Text, string LanguageCode)>();

            // Simple heuristic: Split based on character set (e.g., Latin characters for English)
            var words = mixedText.Split(' ');
            StringBuilder currentSegment = new StringBuilder();
            string currentLang = IsHebrew(words[0]) ? "he-IL" : "en-US";

            foreach (var word in words)
            {
                string lang = IsHebrew(word) ? "he-IL" : "en-US";
                if (lang != currentLang)
                {
                    segments.Add((currentSegment.ToString().Trim(), currentLang));
                    currentSegment.Clear();
                    currentLang = lang;
                }
                currentSegment.Append(word + " ");
            }

            if (currentSegment.Length > 0)
            {
                segments.Add((currentSegment.ToString().Trim(), currentLang));
            }

            return segments;
        }

        private static bool IsHebrew(string word)
        {
            // Implement a check to see if the word contains Hebrew characters
            return word.Any(c => c >= 'à' && c <= 'ú');
        }

    }
}
