using DirectShowLib;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WPFMediaKit.DirectShow.Controls;
using WPFMediaKit.DirectShow.MediaPlayers;

namespace EmotionsRecognition
{
	class MainViewModel : Window, INotifyPropertyChanged
    {

        private bool sliderDrag;
        private bool sliderMediaChange;
        private Uri mediaUriSource;


        // Add your Face subscription key to your environment variables.
        private static string subscriptionKey = Environment.GetEnvironmentVariable("FACE_SUBSCRIPTION_KEY");
        // Add your Face endpoint to your environment variables.
        private string faceEndpoint = Environment.GetEnvironmentVariable("FACE_ENDPOINT");

        private readonly IFaceClient faceClient = new FaceClient(
            new ApiKeyServiceClientCredentials(subscriptionKey),
            new System.Net.Http.DelegatingHandler[] { });

        // The list of detected faces.
        private IList<DetectedFace> faceList;
        // The list of descriptions for the detected faces.
        private string[] faceDescriptions;
        // The resize factor for the displayed image.
        private double resizeFactor;
        private Uri mediaUriElementSource;
        public MainViewModel()
		{
            //this.Closing += MainWindow_Closing; //TUUUUUUUUUUUUUUUUUUUUUUUUUUUU
            //this.mediaUriElement.MediaFailed += MediaUriElement_MediaFailed;
            //this.mediaUriElement.MediaUriPlayer.MediaPositionChanged += MediaUriPlayer_MediaPositionChanged;
            this.mediaUriSource = this.mediaUriElementSource;

            if (MultimediaUtil.VideoInputDevices.Any())
            {
                //cobVideoSource.ItemsSource = MultimediaUtil.VideoInputNames;
                Sources = MultimediaUtil.VideoInputNames;
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
        public IList<string> sources = new List<string> {};

        public IList<string> Sources
        {
            get { return Sources; }
            set { Sources = value; }
        }

        public ObservableCollection<string> LstSources = new ObservableCollection<string> {};


        // Displays the image and calls UploadAndDetectFaces.
        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the image file to scan from the user.
            var openDlg = new Microsoft.Win32.OpenFileDialog();

            openDlg.Filter = "JPEG Image(*.jpg)|*.jpg";
            bool? result = openDlg.ShowDialog(this);

            // Return if canceled.
            if (!(bool)result)
            {
                return;
            }

            // Display the image file.
            string filePath = openDlg.FileName;

            Uri fileUri = new Uri(filePath);
            BitmapImage bitmapSource = new BitmapImage();

            bitmapSource.BeginInit();
            bitmapSource.CacheOption = BitmapCacheOption.None;
            bitmapSource.UriSource = fileUri;
            bitmapSource.EndInit();

            FacePhotoSource = bitmapSource;
            await DetectFacesAsync(filePath, bitmapSource);



        }

        public async Task DetectFacesAsync(string filePath, BitmapImage bitmapSource)
        {
            // Detect any faces in the image.
            Title = "Wyszukiwanie twarzy...";
            faceList = await UploadAndDetectFaces(filePath);
            Title = String.Format(
                "Wyszukiwanie zakończone. Znaleziono {0} twarz(e)", faceList.Count);

            if (faceList.Count > 0)
            {
                // Prepare to draw rectangles around the faces.
                DrawingVisual visual = new DrawingVisual();
                DrawingContext drawingContext = visual.RenderOpen();
                drawingContext.DrawImage(bitmapSource,
                    new Rect(0, 0, bitmapSource.Width, bitmapSource.Height));
                double dpi = bitmapSource.DpiX;
                // Some images don't contain dpi info.
                resizeFactor = (dpi == 0) ? 1 : 96 / dpi;
                faceDescriptions = new String[faceList.Count];

                for (int i = 0; i < faceList.Count; ++i)
                {
                    DetectedFace face = faceList[i];

                    // Draw a rectangle on the face.
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

                    // Store the face description.
                    faceDescriptions[i] = FaceDescription(face);
                    faceDescriptionStatusBarText = faceDescriptions[i];
                }

                drawingContext.Close();

                // Display the image with the rectangle around the face.
                RenderTargetBitmap faceWithRectBitmap = new RenderTargetBitmap(
                    (int)(bitmapSource.PixelWidth * resizeFactor),
                    (int)(bitmapSource.PixelHeight * resizeFactor),
                    96,
                    96,
                    PixelFormats.Pbgra32);

                faceWithRectBitmap.Render(visual);
                FacePhotoSource = faceWithRectBitmap;
            }
        }
        public ImageSource FacePhotoSource = null;
        // Displays the face description when the mouse is over a face rectangle.
        private void FacePhoto_MouseMove(object sender, MouseEventArgs e)
        {
            // If the REST call has not completed, return.
            if (faceList == null)
                return;

            // Find the mouse position relative to the image.
            Point mouseXY = e.GetPosition(this);

            ImageSource imageSource = FacePhotoSource;
            BitmapSource bitmapSource = (BitmapSource)imageSource;

            int FacePhotoActualWidth = 100;

            // Scale adjustment between the actual size and displayed size.
            var scale = FacePhotoActualWidth / (bitmapSource.PixelWidth / resizeFactor);

            // Check if this mouse position is over a face rectangle.
            bool mouseOverFace = false;

            for (int i = 0; i < faceList.Count; ++i)
            {
                FaceRectangle fr = faceList[i].FaceRectangle;
                double left = fr.Left * scale;
                double top = fr.Top * scale;
                double width = fr.Width * scale;
                double height = fr.Height * scale;

                // Display the face description if the mouse is over this face rectangle.
                if (mouseXY.X >= left && mouseXY.X <= left + width &&
                    mouseXY.Y >= top && mouseXY.Y <= top + height)
                {
                    faceDescriptionStatusBarText = faceDescriptions[i];
                    mouseOverFace = true;
                    break;
                }
            }

            // String to display when the mouse is not over a face rectangle.
            // if (!mouseOverFace) faceDescriptionStatusBar.Text = defaultStatusBarText;
        }
        public string faceDescriptionStatusBarText;
        // Uploads the image file and calls DetectWithStreamAsync.
        private async Task<IList<DetectedFace>> UploadAndDetectFaces(string imageFilePath)
        {
            // The list of Face attributes to return.
            IList<FaceAttributeType> faceAttributes =
                new FaceAttributeType[]
                {
            FaceAttributeType.Gender, FaceAttributeType.Age,
            FaceAttributeType.Smile, FaceAttributeType.Emotion,
            FaceAttributeType.Glasses, FaceAttributeType.Hair
                };

            // Call the Face API.
            try
            {
                using (Stream imageFileStream = File.OpenRead(imageFilePath))
                {
                    // The second argument specifies to return the faceId, while
                    // the third argument specifies not to return face landmarks.
                    IList<DetectedFace> faceList =
                        await faceClient.Face.DetectWithStreamAsync(
                            imageFileStream, true, false, faceAttributes);
                    return faceList;
                }
            }
            // Catch and display Face API errors.
            catch (APIErrorException f)
            {
                MessageBox.Show(f.Message); //<----------------------------TUUUUUUUUUUUUUUUUUUUUUUUUUuu
                return new List<DetectedFace>();
            }
            // Catch and display all other errors.
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Błąd");
                return new List<DetectedFace>();
            }
        }
        private Visibility mediaUriElementVisibility = Visibility.Hidden;
        private Visibility cameraCaptureElementVisibility = Visibility.Hidden;
        int cobVideoSourceSelectedIndex = 0;
        private void SetCameraCaptureElementVisible(bool visible) /////////// TUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUU
        {
            cameraCaptureElementVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
            mediaUriElementVisibility = !visible ? Visibility.Visible : Visibility.Collapsed;
            
            if (visible)
            {
              //  btnStop_Click(null, null);
            }
            else
            {
                cobVideoSourceSelectedIndex = -1;
            }
        }


        // Creates a string out of the attributes describing the face.
        private string FaceDescription(DetectedFace face)
        {
            StringBuilder sb = new StringBuilder();

            //sb.Append("Twarz: ");

            // Add the gender, age, and smile.
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
            //  if (emotionScores.Anger >= 0.1f) 
            sb.Append("\n" +
            String.Format("Złość {0:F1}%, ", emotionScores.Anger * 100));
            // if (emotionScores.Contempt >= 0.1f) 
            sb.Append("\n" +
                String.Format("Pogarda {0:F1}%, ", emotionScores.Contempt * 100));
            //  if (emotionScores.Disgust >= 0.1f) 
            sb.Append("\n" +
                String.Format("Obrzydzenie {0:F1}%, ", emotionScores.Disgust * 100));
            // if (emotionScores.Fear >= 0.1f) 
            sb.Append("\n" +
                String.Format("Strach {0:F1}%, ", emotionScores.Fear * 100));
            // if (emotionScores.Happiness >= 0.1f) 
            sb.Append("\n" +
                String.Format("Szczęście {0:F1}%, ", emotionScores.Happiness * 100));
            //  if (emotionScores.Neutral >= 0.1f) 
            sb.Append("\n" +
                String.Format("Neutralny {0:F1}%, ", emotionScores.Neutral * 100));
            //  if (emotionScores.Sadness >= 0.1f) 
            sb.Append("\n" +
                String.Format("Smutek {0:F1}%, ", emotionScores.Sadness * 100));
            // if (emotionScores.Surprise >= 0.1f) 
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
                em.ID = list[list.Count - 1] + 1;

                db.RecognizedEmotions.Add(em);

                db.SaveChanges();

            }


            // Return the built string.
            return sb.ToString();
        }

    
        //private void MediaUriElement_MediaFailed(object sender, WPFMediaKit.DirectShow.MediaPlayers.MediaFailedEventArgs e) // TUUUUUUUUU
        //{
        //    this.Dispatcher.BeginInvoke(new Action(() => errorText.Text = e.Message));
        //}

        //private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) // TUUUUUUUUU
        //{
        //    mediaUriElement.Close();
        //}

        

        private MediaUriPlayer _player;

        private System.Timers.Timer tm;
        private bool isTextVisible = false;
        private int textID;

        public event PropertyChangedEventHandler PropertyChanged;
        public string polishSentence = "Tu będzie wyświetlany tekst";
        private async void btnStart_Click(object sender, RoutedEventArgs e) // TUUUUUUUUU
        {

            using (var db = new EmotionsRecognitionDbContext())
            {
                // Display from the database
                var rand = new Random();
                textID = rand.Next(701);

                var query = from b in db.Texts
                            where b.ID == textID
                            select b;


                foreach (var item in query)
                {

                    polishSentence = item.PolishSentence;
                }


            }
            if (!isTextVisible)
            {
                isTextVisible = true;
                return;
            }

            //tm = new System.Timers.Timer(2000);
            //tm.Elapsed += OnTimedTakePictureEvent;
            //tm.AutoReset = true;
            //tm.Enabled = true;


            //mediaUriElement.Stop();
            //mediaUriElement.Close();
            //SetPlayButtons(false);

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

            int cameraCaptureElementActualWidth = 90;
            int cameraCaptureElementActualHeight = 50;
            byte[] captureData;
            RenderTargetBitmap bmp1 = new RenderTargetBitmap((int)cameraCaptureElementActualWidth, (int)cameraCaptureElementActualHeight, 96, 96,
       PixelFormats.Default);
            //do zdjec
            /*
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

            //string relativePath = "/images/picture.jpg";
            Uri serverUri = new Uri(server);

            // needs UriKind arg, or UriFormatException is thrown
            Uri relativeUri = new Uri(filePath, UriKind.Relative);

            // Uri(Uri, Uri) is the preferred constructor in this case
            Uri fullUri = new Uri(serverUri, relativeUri);

            //Uri fileUri = new Uri();
            BitmapImage bitmapSource = new BitmapImage();

            bitmapSource.BeginInit();
            bitmapSource.CacheOption = BitmapCacheOption.None;
            bitmapSource.UriSource = fullUri;
            bitmapSource.EndInit();

            FacePhotoSource = bitmapSource;
            await DetectFacesAsync(filePath, bitmapSource);
            */
        }

        private static void OnTimedTakePictureEvent(Object source, ElapsedEventArgs e)
        {
            Console.WriteLine("The Elapsed event was raised at {0:HH:mm:ss.fff}",
                              e.SignalTime);
        }

        private void GrabScreenShot(IntPtr backBuffer)
        {
            // The screenshot is in the backBuffer.
            D3DImage d3d = new D3DImage();
            //D3DImageUtils.SetBackBufferWithLock(d3d, backBuffer);
            // Display or process the d3d.
            FacePhotoSource = (ImageSource)d3d;
        }

        //private void MediaUriPlayer_MediaPositionChanged(object sender, EventArgs e)
        //{
        //    if (sliderDrag)
        //        return;
        //    this.Dispatcher.BeginInvoke(new Action(ChangeSlideValue), null);
        //}
        public DsDevice cameraCaptureElementVideoCaptureDevice = null;

        private void cobVideoSource_SelectionChanged() // TUUUUUUUUU
        {
            if (cobVideoSourceSelectedIndex < 0)
                return;
            SetCameraCaptureElementVisible(true);
            cameraCaptureElementVideoCaptureDevice = MultimediaUtil.VideoInputDevices[cobVideoSourceSelectedIndex];
        }

    }
}
