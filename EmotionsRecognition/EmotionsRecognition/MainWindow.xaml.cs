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

            this.Closing += MainWindow_Closing; //TUUUUUUUUUUUUUUUUUUUUUUUUUUUU
            this.mediaUriElement.MediaFailed += MediaUriElement_MediaFailed;
            this.mediaUriElement.MediaUriPlayer.MediaPositionChanged += MediaUriPlayer_MediaPositionChanged;
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

            FacePhoto.Source = bitmapSource;
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
                    faceDescriptionStatusBar.Text = faceDescriptions[i];
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
                FacePhoto.Source = faceWithRectBitmap;

                // Set the status bar text.
                //faceDescriptionStatusBar.Text = defaultStatusBarText;
            }
        }

        // Displays the face description when the mouse is over a face rectangle.
        private void FacePhoto_MouseMove(object sender, MouseEventArgs e)
        {
            // If the REST call has not completed, return.
            if (faceList == null)
                return;

            // Find the mouse position relative to the image.
            Point mouseXY = e.GetPosition(FacePhoto);

            ImageSource imageSource = FacePhoto.Source;
            BitmapSource bitmapSource = (BitmapSource)imageSource;

            // Scale adjustment between the actual size and displayed size.
            var scale = FacePhoto.ActualWidth / (bitmapSource.PixelWidth / resizeFactor);

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
                    faceDescriptionStatusBar.Text = faceDescriptions[i];
                    mouseOverFace = true;
                    break;
                }
            }

            // String to display when the mouse is not over a face rectangle.
           // if (!mouseOverFace) faceDescriptionStatusBar.Text = defaultStatusBarText;
        }
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
        private void SetCameraCaptureElementVisible(bool visible) /////////// TUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUU
        {
            cameraCaptureElement.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            mediaUriElement.Visibility = !visible ? Visibility.Visible : Visibility.Collapsed;
            //btnStop.IsEnabled = !visible;
            //btnPause.IsEnabled = !visible;
            //slider.IsEnabled = !visible;
            if (visible)
            {
                btnStop_Click(null, null);
            }
            else
            {
                cobVideoSource.SelectedIndex = -1;
            }
        }

        private void SetPlayButtons(bool playing)
        {
            //if (playing)
            //{
            //    btnPause.Content = "Pause";
            //}
            //else
            //{
            //    btnPause.Content = "Play";
            //}
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
            //sb.Append(", ");
            //sb.Append(String.Format("uśmiech {0:F1}%, ", face.FaceAttributes.Smile * 100));
           // sb.Append("\n");
            // Add the emotions. Display all emotions over 10%. - nie
           // sb.Append("Emocja: ");
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
                em.ID = list[list.Count-1]+1 ;

                db.RecognizedEmotions.Add(em);

                db.SaveChanges();

            }

            // Add glasses.
            //switch (face.FaceAttributes.Glasses)
            //{
            //    case GlassesType.NoGlasses:
            //            sb.Append("\n" + "Brak okularów");
            //        break;

            //    case GlassesType.ReadingGlasses:
            //        sb.Append("\n" + "Okulary korekcyjne");
            //        break;
            //    case GlassesType.Sunglasses:
            //        sb.Append("\n" + "Okulary przeciwsłoneczne");
            //        break;
            //    case GlassesType.SwimmingGoggles:
            //        sb.Append("\n" + "Gogle pływackie");
            //        break;
            //    default:
            //        sb.Append(face.FaceAttributes.Glasses);
            //        break;
            //}

            //sb.Append(", ");

            //// Add hair.
            //sb.Append("\nWłosy: ");

            //// Display baldness confidence if over 1%.
            //if (face.FaceAttributes.Hair.Bald >= 0.01f)
            //    sb.Append(String.Format("\nłysina {0:F1}% ", face.FaceAttributes.Hair.Bald * 100));

            //// Display all hair color attributes over 10%.
            //IList<HairColor> hairColors = face.FaceAttributes.Hair.HairColor;
            //foreach (HairColor hairColor in hairColors)
            //{
            //    if (hairColor.Confidence >= 0.1f)
            //    {
            //        //switch (HairColorType)
            //        //{
            //        //    case HairColorType.Unknown:
            //        //        break;
            //        //    case HairColorType.White:
            //        //        break;
            //        //    case HairColorType.Gray:
            //        //        break;
            //        //    case HairColorType.Blond:
            //        //        break;
            //        //    case HairColorType.Brown:
            //        //        break;
            //        //    case HairColorType.Red:
            //        //        break;
            //        //    case HairColorType.Black:
            //        //        break;
            //        //    case HairColorType.Other:
            //        //        break;
            //        //    default:
            //        //        break;
            //        //}

            //        sb.Append(hairColor.Color.ToString());

            //        sb.Append(String.Format(" {0:F1}% ", hairColor.Confidence * 100));
            //    }
            //}

            // Return the built string.
            return sb.ToString();
        }

        //private void btnOpen_Click(object sender, RoutedEventArgs e)
        //{
        //    var dlg = new OpenFileDialog();
        //    var result = dlg.ShowDialog();
        //    if (result != true)
        //        return;
        //    errorText.Text = null;
        //    SetCameraCaptureElementVisible(false);
        //    mediaUriElement.Source = new Uri(dlg.FileName);
        //    SetPlayButtons(true);
        //}

        private void MediaUriElement_MediaFailed(object sender, WPFMediaKit.DirectShow.MediaPlayers.MediaFailedEventArgs e) // TUUUUUUUUU
        {
            this.Dispatcher.BeginInvoke(new Action(() => errorText.Text = e.Message));
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) // TUUUUUUUUU
        {
            mediaUriElement.Close();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e) // TUUUUUUUUU
        {
            mediaUriElement.Stop();
            SetPlayButtons(false);  
        }

        private MediaUriPlayer _player;

        private System.Timers.Timer tm;
        private bool isTextVisible = false;
        private int textID;
        private async void btnStart_Click(object sender, RoutedEventArgs e) // TUUUUUUUUU
        {
           
            using (var db = new EmotionsRecognitionDbContext())
            {
                // Display from the database
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

            //tm = new System.Timers.Timer(2000);
            //tm.Elapsed += OnTimedTakePictureEvent;
            //tm.AutoReset = true;
            //tm.Enabled = true;


            mediaUriElement.Stop();
            mediaUriElement.Close();
            SetPlayButtons(false);

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


            byte[] captureData;
            RenderTargetBitmap bmp1 = new RenderTargetBitmap((int)cameraCaptureElement.ActualWidth, (int)cameraCaptureElement.ActualHeight, 96, 96,
       PixelFormats.Default);
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

            FacePhoto.Source = bitmapSource;
            await DetectFacesAsync(filePath, bitmapSource);

            //DialogResult = true;



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
            FacePhoto.Source = (ImageSource)d3d;
        }

        //private void btnPause_Click(object sender, RoutedEventArgs e) // TUUUUUUUUU
        //{
        //    bool playing = mediaUriElement.IsPlaying;
        //    if (playing)
        //        mediaUriElement.Pause();
        //    else
        //        mediaUriElement.Play();
        //    SetPlayButtons(!playing);
        //}

        private void MediaUriPlayer_MediaPositionChanged(object sender, EventArgs e)
        {
            if (sliderDrag)
                return;
            this.Dispatcher.BeginInvoke(new Action(ChangeSlideValue), null);
        }

        private void ChangeSlideValue()
        {
            //if (sliderDrag)
            //    return;

            //sliderMediaChange = true;
            //double perc = (double)mediaUriElement.MediaPosition / mediaUriElement.MediaDuration;
            //slider.Value = slider.Maximum * perc;
            //sliderMediaChange = false;
        }

        private void ChangeMediaPosition()
        {
        //    if (sliderMediaChange)
        //        return;

        //    sliderDrag = true;
        //    double perc = slider.Value / slider.Maximum;
        //    mediaUriElement.MediaPosition = (long)(mediaUriElement.MediaDuration * perc);
        //    sliderDrag = false;
        }

        private void slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sliderMediaChange)
                return;

            this.Dispatcher.BeginInvoke(new Action(ChangeMediaPosition), null);
        }

        private void cobVideoSource_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) // TUUUUUUUUU
        {
            if (cobVideoSource.SelectedIndex < 0)
                return;
            SetCameraCaptureElementVisible(true);
            cameraCaptureElement.VideoCaptureDevice = MultimediaUtil.VideoInputDevices[cobVideoSource.SelectedIndex];
        }
    }
}
