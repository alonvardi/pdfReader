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
using System.Text.RegularExpressions;
using System.Media;
using NAudio.Wave;

namespace pdfReader
{
    public sealed partial class MainWindow : Window
    {

        private int _currentPageIndex = 0;
        private PdfDocument _pdfDocument;
        private List<Word> processedWords;
        private static double speechSpeed = 1;
        private static bool talk = false;
        private static IWavePlayer waveOutDevice;
        private WaveStream audioStream;

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
                DrawingCanvas.Children.Clear();
                // Scroll to the bottom of the new page
                PdfScrollViewer.ChangeView(null, PdfScrollViewer.ScrollableHeight - 1, null);

                // User scrolled to the top, go to the previous page
                _currentPageIndex--;
                await RenderPdfPageAsync(_currentPageIndex);


            }
            else if (PdfScrollViewer.VerticalOffset == PdfScrollViewer.ScrollableHeight && _currentPageIndex < _pdfDocument.PageCount - 1)
            {
                DrawingCanvas.Children.Clear();
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

                            //Debug.WriteLine(newWord.toString());
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

            var word = FindWordAtCoordinates(x, y);

            if (word != null)
            {
                Debug.WriteLine($"clicked on {word.getText()}");

                // speech
                List<Word> words = findSentenceInCoordinates(x, y);
                markLines(words);
                var segments = SegmentText(retireveStringFromListWords(words));
                Debug.WriteLine("Start talk");
                talk = true;
                await SpeakText(segments);
                Debug.WriteLine("finish talk");

                loopTillOver(words.Last());



                //Debug.WriteLine($"{word.getText()}");
            }
        }



        private void markLines(List<Word> words)
        {
            DrawingCanvas.Children.Clear();
            int space = 15;
            int x, y, width, height, startSentence = 0;
            Microsoft.UI.Xaml.Shapes.Rectangle rectangle = null;

            int minPoint = Math.Min(words.ElementAt(0).getBoundingBox().getX1(), words.ElementAt(0).getBoundingBox().getX2());
            int maxPoint = Math.Max(words.ElementAt(0).getBoundingBox().getX1(), words.ElementAt(0).getBoundingBox().getX2());
            x = minPoint;
            y = words.ElementAt(0).getBoundingBox().getY1();
            width = maxPoint - minPoint;
            height = words.ElementAt(0).getBoundingBox().getY2() - words.ElementAt(0).getBoundingBox().getY1();
            //Debug.WriteLine($"(x1 {x},y1 {y}) (x2 {words.ElementAt(0).getBoundingBox().getX2()} y2 {words.ElementAt(0).getBoundingBox().getY2()}) : {words.ElementAt(0).getText()}");
            for (int i = 1; i < words.Count; i++)
            {
                if (words.ElementAt(i).getBoundingBox().getY1() - words.ElementAt(i - 1).getBoundingBox().getY1() > -space && (words.ElementAt(i).getBoundingBox().getY1() - words.ElementAt(i - 1).getBoundingBox().getY1()) < space)
                {
                    minPoint = Math.Min(Math.Min(words.ElementAt(startSentence).getBoundingBox().getX1(), words.ElementAt(startSentence).getBoundingBox().getX2()), Math.Min(words.ElementAt(i).getBoundingBox().getX1(), words.ElementAt(i).getBoundingBox().getX2()));
                    maxPoint = Math.Max(Math.Max(words.ElementAt(startSentence).getBoundingBox().getX1(), words.ElementAt(startSentence).getBoundingBox().getX2()), Math.Max(words.ElementAt(i).getBoundingBox().getX1(), words.ElementAt(i).getBoundingBox().getX2()));
                    x = minPoint;
                    y = words.ElementAt(i).getBoundingBox().getY1();
                    width = maxPoint - minPoint;
                }
                else
                {
                    //Debug.WriteLine($"draw square from {x},{y} width {width} height {height}");

                    rectangle = new Microsoft.UI.Xaml.Shapes.Rectangle
                    {
                        Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 0, 0)),
                        Width = width,
                        Height = height,
                        Margin = new Thickness(x, y, 0, 0)
                    };
                    DrawingCanvas.Children.Add(rectangle);

                    minPoint = Math.Min(Math.Min(words.ElementAt(startSentence).getBoundingBox().getX1(), words.ElementAt(startSentence).getBoundingBox().getX2()), Math.Min(words.ElementAt(i).getBoundingBox().getX1(), words.ElementAt(i).getBoundingBox().getX2()));
                    maxPoint = Math.Max(Math.Max(words.ElementAt(startSentence).getBoundingBox().getX1(), words.ElementAt(startSentence).getBoundingBox().getX2()), Math.Max(words.ElementAt(i).getBoundingBox().getX1(), words.ElementAt(i).getBoundingBox().getX2()));
                    x = minPoint;
                    y = words.ElementAt(i).getBoundingBox().getY1();
                    width = maxPoint - minPoint;
                    height = words.ElementAt(i).getBoundingBox().getY2() - words.ElementAt(i).getBoundingBox().getY1();
                    startSentence = i;

                }



                //Debug.WriteLine($"(x1 {x},y1 {y}) (x2 {words.ElementAt(i).getBoundingBox().getX2()},y2 {words.ElementAt(i).getBoundingBox().getY2()}) width {width} height {height} : {words.ElementAt(i).getText()}");
                //Debug.WriteLine($"text- {words.ElementAt(i).getText()}(x1-{words.ElementAt(i).getBoundingBox().getX1()},y1-{words.ElementAt(i).getBoundingBox().getY1()},x2-{words.ElementAt(i).getBoundingBox().getX2()},y2-{words.ElementAt(i).getBoundingBox().getY2()})");


            }

            //Debug.WriteLine($"draw square from x1 y1 {x},{y} x2 y2 {x + width} height {y + height}");
            rectangle = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 0, 0)),
                Width = width,
                Height = height,
                Margin = new Thickness(x, y, 0, 0)
            };
            DrawingCanvas.Children.Add(rectangle);
        }
        private String retireveStringFromListWords(List<Word> words)
        {
            String text = "";
            foreach (Word word in words.Take(words.Count - 1))
            {
                if (word.getText().IndexOf(",") != -1)
                {
                    text += word.getText().Replace(",", "");
                }
                else
                    text += word.getText() + " ";
            }
            return text;
        }
        private List<Word> findNextSentenceOfWord(Word word)
        {
            var words = new List<Word>();

            if (processedWords == null)
            {
                Debug.WriteLine("ProcessedWords NULL!");
                return null;
            }
            int indexOfNextWord = processedWords.IndexOf(word) + 1;
            if (indexOfNextWord >= processedWords.Count)
                return null;
            words.Add(processedWords.ElementAt(indexOfNextWord));
            int nextWord = processedWords.IndexOf(words.Last()) + 1;

            while (IsValidString(words.Last().getText()) && nextWord < processedWords.Count - 1)
            {
                nextWord = processedWords.IndexOf(words.Last()) + 1;
                words.Add(processedWords.ElementAt(nextWord));
            }

            //Debug.WriteLine("sentence ");
            //print all elements 
            foreach (Word wrd in words)
            {
                Debug.Write(wrd.getText() + " ");
            }
            Debug.WriteLine("");
            return words;
        }
        private List<Word> findSentenceInCoordinates(int x, int y)
        {
            var words = new List<Word>();

            if (processedWords == null)
            {
                Debug.WriteLine("ProcessedWords NULL!");
                return null;
            }
            words.Add(FindWordAtCoordinates(x, y));
            int nextWord = processedWords.IndexOf(words.Last()) + 1;

            while (IsValidString(words.Last().getText()) && nextWord < processedWords.Count - 1)
            {
                nextWord = processedWords.IndexOf(words.Last()) + 1;
                words.Add(processedWords.ElementAt(nextWord));
            }

            //Debug.WriteLine("sentence ");
            //print all elements 
            foreach (Word word in words)
            {
                Debug.Write(word.getText() + " ");
            }
            Debug.WriteLine("");
            return words;
        }

        bool IsValidString(string input)
        {
            return !Regex.IsMatch(input, @"\.([^\s](?!.|$))*$");
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
       private async void loopTillOver(Word word)
        {
            while (word != null && talk)
            {
                List<Word> sentence = findNextSentenceOfWord(word);
                if (sentence == null)
                    break;
                markLines(sentence);
                var segments = SegmentText(retireveStringFromListWords(sentence));

                await SpeakText(segments);

                word = sentence.Last();

            }
            //clear mark
            DrawingCanvas.Children.Clear();
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
            var config = new AudioConfig
            {
                AudioEncoding = AudioEncoding.Linear16,
                SpeakingRate = speechSpeed
            };
            var response = await client.SynthesizeSpeechAsync(input, voice, config);

            // Play the audio
            await PlayAudioAsync(response.AudioContent.ToByteArray());
        }
        private static async Task PlayAudioAsync(byte[] audioData)
        {
            using (var ms = new MemoryStream(audioData))
            using (var rdr = new RawSourceWaveStream(ms, new WaveFormat(12000, 32,1)))
            using (waveOutDevice = new WaveOutEvent())
            {
                waveOutDevice.Init(rdr);
                waveOutDevice.Play();

                while (waveOutDevice.PlaybackState == PlaybackState.Playing)
                {
                    await Task.Delay(100); // Wait for the playback to finish
                }
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

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            talk = false;
            waveOutDevice?.Stop();
        }
        private void SpeechSpeedTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(SpeechSpeedTextBox.Text, out double newSpeed))
            {
                if (newSpeed >= 0.5 && newSpeed <= 3.0)
                {
                    speechSpeed = newSpeed;
                }
                else
                {
                    // Handle invalid speed (outside range)
                    SpeechSpeedTextBox.Text = speechSpeed.ToString();
                }
            }
            else
            {
                // Handle invalid input (not a number)
                SpeechSpeedTextBox.Text = speechSpeed.ToString();
            }
        }


/*        // save audio to file 
        static async Task SynthesizeAndSaveSegment(string text, string languageCode, string audioFilePath)
        {
            TextToSpeechClient client = TextToSpeechClient.Create();
            var input = new SynthesisInput { Text = text };
            var voice = new VoiceSelectionParams
            {
                LanguageCode = languageCode,
                SsmlGender = SsmlVoiceGender.Neutral
            };
            var config = new AudioConfig
            {
                AudioEncoding = AudioEncoding.Linear16,
                SpeakingRate = speechSpeed
            };
            var response = await client.SynthesizeSpeechAsync(input, voice, config);

            using (var fileStream = new FileStream(audioFilePath, FileMode.Create, FileAccess.Write))
            {
                fileStream.Write(response.AudioContent.ToByteArray());
            }
        }*/

    }
}
