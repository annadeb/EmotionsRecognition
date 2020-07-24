using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Microsoft.Win32;
using WPFMediaKit.DirectShow.Controls;
using WPFMediaKit.DirectShow.MediaPlayers;

namespace EmotionsRecognition
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Uri mediaUriSource;
        private static string subscriptionKey = Environment.GetEnvironmentVariable("FACE_SUBSCRIPTION_KEY");
        private string faceEndpoint = Environment.GetEnvironmentVariable("FACE_ENDPOINT");

        private readonly IFaceClient faceClient = new FaceClient(
            new ApiKeyServiceClientCredentials(subscriptionKey),
            new System.Net.Http.DelegatingHandler[] { });

        private IList<DetectedFace> faceList;
        private string[] faceDescriptions;
        private double resizeFactor;

        private const string defaultStatusBarText =
            "Najedź myszką na twarz, aby zobaczyć jej opis.";

        public MainWindow()
        {
            try
            {
                InitializeComponent();
            }
            catch(Exception e)
            {
               Console.WriteLine(e.Message);
            }

            this.Closing += MainWindow_Closing;
            this.mediaUriElement.MediaFailed += MediaUriElement_MediaFailed;
            this.mediaUriSource = this.mediaUriElement.Source;

            if (MultimediaUtil.VideoInputDevices.Any())
            {
                cobVideoSource.ItemsSource = MultimediaUtil.VideoInputNames;
            }
            SetCameraCaptureElementVisible(false);

            if (Uri.IsWellFormedUriString(faceEndpoint, UriKind.Absolute))
            {
                faceClient.Endpoint = faceEndpoint;
            }
            else
            {
                MessageBox.Show(faceEndpoint,
                    "Invalid URI", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openDlg = new Microsoft.Win32.OpenFileDialog();

            openDlg.Filter = "JPEG Image(*.jpg)|*.jpg";
            bool? result = openDlg.ShowDialog(this);

            if (!(bool)result)
            {
                return;
            }
            string filePath = openDlg.FileName;

            Uri fileUri = new Uri(filePath);
            BitmapImage bitmapSource = new BitmapImage();

            bitmapSource.BeginInit();
            bitmapSource.CacheOption = BitmapCacheOption.None;
            bitmapSource.UriSource = fileUri;
            bitmapSource.EndInit();

            FacePhoto.Source = bitmapSource;
            await DetectFacesAsync(filePath, bitmapSource);
        }

        public async Task DetectFacesAsync(string filePath, BitmapImage bitmapSource)
        {
            Title = "Wyszukiwanie twarzy...";
            faceList = await UploadAndDetectFaces(filePath);
            Title = String.Format(
                "Wyszukiwanie zakończone. Znaleziono {0} twarz(e)", faceList.Count);

            if (faceList.Count > 0)
            {
                DrawingVisual visual = new DrawingVisual();
                DrawingContext drawingContext = visual.RenderOpen();
                drawingContext.DrawImage(bitmapSource,
                    new Rect(0, 0, bitmapSource.Width, bitmapSource.Height));
                double dpi = bitmapSource.DpiX;
                resizeFactor = (dpi == 0) ? 1 : 96 / dpi;
                faceDescriptions = new String[faceList.Count];

                for (int i = 0; i < faceList.Count; ++i)
                {
                    DetectedFace face = faceList[i];
                    drawingContext.DrawRectangle(
                        Brushes.Transparent,
                        new Pen(Brushes.Red, 2),
                        new Rect(
                            face.FaceRectangle.Left * resizeFactor,
                            face.FaceRectangle.Top * resizeFactor,
                            face.FaceRectangle.Width * resizeFactor,
                            face.FaceRectangle.Height * resizeFactor
                            )
                    );
                    faceDescriptions[i] = FaceDescription(face);
                    faceDescriptionStatusBar.Text = faceDescriptions[i];
                }

                drawingContext.Close();

                RenderTargetBitmap faceWithRectBitmap = new RenderTargetBitmap(
                    (int)(bitmapSource.PixelWidth * resizeFactor),
                    (int)(bitmapSource.PixelHeight * resizeFactor),
                    96,
                    96,
                    PixelFormats.Pbgra32);

                faceWithRectBitmap.Render(visual);
                FacePhoto.Source = faceWithRectBitmap;
            }
        }

        private void FacePhoto_MouseMove(object sender, MouseEventArgs e)
        {
            if (faceList == null)
                return;

            Point mouseXY = e.GetPosition(FacePhoto);

            ImageSource imageSource = FacePhoto.Source;
            BitmapSource bitmapSource = (BitmapSource)imageSource;

            var scale = FacePhoto.ActualWidth / (bitmapSource.PixelWidth / resizeFactor);

            bool mouseOverFace = false;

            for (int i = 0; i < faceList.Count; ++i)
            {
                FaceRectangle fr = faceList[i].FaceRectangle;
                double left = fr.Left * scale;
                double top = fr.Top * scale;
                double width = fr.Width * scale;
                double height = fr.Height * scale;

                if (mouseXY.X >= left && mouseXY.X <= left + width &&
                    mouseXY.Y >= top && mouseXY.Y <= top + height)
                {
                    faceDescriptionStatusBar.Text = faceDescriptions[i];
                    mouseOverFace = true;
                    break;
                }
            }
        }

        private async Task<IList<DetectedFace>> UploadAndDetectFaces(string imageFilePath)
        {
            IList<FaceAttributeType> faceAttributes =
                new FaceAttributeType[]
                {
            FaceAttributeType.Gender, FaceAttributeType.Age,
            FaceAttributeType.Smile, FaceAttributeType.Emotion,
            FaceAttributeType.Glasses, FaceAttributeType.Hair
                };

            try
            {
                using (Stream imageFileStream = File.OpenRead(imageFilePath))
                {
                    IList<DetectedFace> faceList =
                        await faceClient.Face.DetectWithStreamAsync(
                            imageFileStream, true, false, faceAttributes);
                    return faceList;
                }
            }
            catch (APIErrorException f)
            {
                MessageBox.Show(f.Message);
                return new List<DetectedFace>();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Błąd");
                return new List<DetectedFace>();
            }
        }
        private void SetCameraCaptureElementVisible(bool visible)
        {
            cameraCaptureElement.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            mediaUriElement.Visibility = !visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
            {
            }
            else
            {
                cobVideoSource.SelectedIndex = -1;
            }
        }

        private string FaceDescription(DetectedFace face)
        {
            StringBuilder sb = new StringBuilder();
            if (face.FaceAttributes.Gender == Gender.Female)
            {
                sb.Append("Płeć: kobieta");
            }
            else
            {
                sb.Append("Płeć: mężczyzna");
            }

            sb.Append(", ");
            sb.Append("Wiek: " + face.FaceAttributes.Age);
            sb.Append("\n");
            Emotion emotionScores = face.FaceAttributes.Emotion;
                sb.Append("\n" +
                String.Format("Złość {0:F1}%, ", emotionScores.Anger * 100));
            sb.Append("\n" +
                String.Format("Pogarda {0:F1}%, ", emotionScores.Contempt * 100));
            sb.Append("\n" +
                String.Format("Obrzydzenie {0:F1}%, ", emotionScores.Disgust * 100));
            sb.Append("\n" +
                String.Format("Strach {0:F1}%, ", emotionScores.Fear * 100));
            sb.Append("\n" +
                String.Format("Szczęście {0:F1}%, ", emotionScores.Happiness * 100));
            sb.Append("\n" +
                String.Format("Neutralny {0:F1}%, ", emotionScores.Neutral * 100));
            sb.Append("\n" +
                String.Format("Smutek {0:F1}%, ", emotionScores.Sadness * 100));
            sb.Append("\n" +
                String.Format("Zaskoczenie {0:F1}%, ", emotionScores.Surprise * 100));

            using (var db = new EmotionsRecognitionDbContext())
            {
                RecognizedEmotion em = new RecognizedEmotion();
                em.Anger = (float)emotionScores.Anger;
                em.Contempt = (float)emotionScores.Contempt;
                em.Disgust = (float)emotionScores.Disgust;
                em.Fear = (float)emotionScores.Fear;
                em.Happiness = (float)emotionScores.Happiness;
                em.Neutral = (float)emotionScores.Neutral;
                em.Sadness = (float)emotionScores.Sadness;
                em.Surprise = (float)emotionScores.Surprise;
                em.TextID = textID;
                if (face.FaceAttributes.Gender == Gender.Female)
                {
                    em.Gender = "Kobieta";
                }
                else
                {
                    em.Gender = "Mężczyzna";
                }
                em.Age = (int)face.FaceAttributes.Age;
                var query = from b in db.RecognizedEmotions
                            select b;
                List<int> list = new List<int>();
                foreach (var item in query)
                {
                    list.Add(item.ID);
                }
                em.ID = list[list.Count-1]+1 ;

                db.RecognizedEmotions.Add(em);

                db.SaveChanges();
            }
            return sb.ToString();
        }

        private void MediaUriElement_MediaFailed(object sender, WPFMediaKit.DirectShow.MediaPlayers.MediaFailedEventArgs e) // TUUUUUUUUU
        {
            this.Dispatcher.BeginInvoke(new Action(() => errorText.Text = e.Message));
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            mediaUriElement.Close();
        }

        private MediaUriPlayer _player;

        private bool isTextVisible = false;
        private int textID;
        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
           
            using (var db = new EmotionsRecognitionDbContext())
            {
                var rand = new Random();
                textID = rand.Next(701);

                var query = from b in db.Texts
                            where b.ID == textID
                            select b ;

                foreach (var item in query)
                {
                    textTextBlock.Text = item.PolishSentence;
                }
            }
            if (!isTextVisible)
            {
                isTextVisible = true;
                return;
            }

            mediaUriElement.Stop();
            mediaUriElement.Close();

            IntPtr backBuffer = new IntPtr();
            _player = new MediaUriPlayer();
            _player.EnsureThread(ApartmentState.MTA);
            _player.NewAllocatorFrame += () => GrabScreenShot(backBuffer);
            _player.NewAllocatorSurface += (s, b) => backBuffer = b;
            _player.Dispatcher.BeginInvoke(new Action(() =>
            {
                _player.AudioDecoder = null;
                _player.AudioRenderer = null;
                _player.Source = mediaUriSource;
                _player.MediaPosition = 10000000;
                _player.Pause();
            }));

            RenderTargetBitmap bmp1 = new RenderTargetBitmap((int)cameraCaptureElement.ActualWidth, (int)cameraCaptureElement.ActualHeight, 96, 96, PixelFormats.Default);
            bmp1.Render(cameraCaptureElement);
            DateTime dt = DateTime.Now;
            var date = dt.ToString().Replace(" ", "-").Replace(":", "-");
            var filePath = ".\\images\\image" + date + ".jpg";
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp1));

            using (var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
            {
                encoder.Save(fileStream);
            }
            string server = "C:\\Users\\Anna\\Desktop\\Studia\\MAGISTERKA_1\\Biometria\\Projekt\\EmotionsRecognition\\EmotionsRecognition\\EmotionsRecognition\\bin\\Debug\\";
            Uri serverUri = new Uri(server);
            Uri relativeUri = new Uri(filePath, UriKind.Relative);

            Uri fullUri = new Uri(serverUri, relativeUri);
            BitmapImage bitmapSource = new BitmapImage();

            bitmapSource.BeginInit();
            bitmapSource.CacheOption = BitmapCacheOption.None;
            bitmapSource.UriSource = fullUri;
            bitmapSource.EndInit();

            FacePhoto.Source = bitmapSource;
            await DetectFacesAsync(filePath, bitmapSource);
        }

        private static void OnTimedTakePictureEvent(Object source, ElapsedEventArgs e)
        {
            Console.WriteLine("The Elapsed event was raised at {0:HH:mm:ss.fff}",
                              e.SignalTime);
        }

        private void GrabScreenShot(IntPtr backBuffer)
        {
            D3DImage d3d = new D3DImage();
            FacePhoto.Source = (ImageSource)d3d;
        }

        private void cobVideoSource_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) // TUUUUUUUUU
        {
            if (cobVideoSource.SelectedIndex < 0)
                return;
            SetCameraCaptureElementVisible(true);
            cameraCaptureElement.VideoCaptureDevice = MultimediaUtil.VideoInputDevices[cobVideoSource.SelectedIndex];
        }

        private void hideCameraCb_Checked(object sender, RoutedEventArgs e)
        {
            rectangle.Visibility = Visibility.Visible;
            FacePhoto.Visibility = Visibility.Hidden;
        }

        private void hideCameraCb_Unchecked(object sender, RoutedEventArgs e)
        {
            rectangle.Visibility = Visibility.Hidden;
            FacePhoto.Visibility = Visibility.Visible;
        }
    }
}
