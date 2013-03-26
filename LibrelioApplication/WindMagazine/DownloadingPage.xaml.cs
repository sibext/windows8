﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Storage.BulkAccess;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Net.Http;
using System.Text;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using MuPDFWinRT;
using Windows.ApplicationModel.Store;
using Windows.Data.Xml.Dom;

// The Basic Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234237

namespace LibrelioApplication
{
    /// <summary>
    /// A basic page that provides characteristics common to most applications.
    /// </summary>
    public sealed partial class DownloadingPage : LibrelioApplication.Common.LayoutAwarePage
    {
        CancellationTokenSource cts;

        IList<string> links = new List<string>();
        StorageFolder folder = null;

        public DownloadingPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Populates the page with content passed during navigation.  Any saved state is also
        /// provided when recreating a page from a prior session.
        /// </summary>
        /// <param name="navigationParameter">The parameter value passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested.
        /// </param>
        /// <param name="pageState">A dictionary of state preserved by this page during an earlier
        /// session.  This will be null the first time a page is visited.</param>
        protected override void LoadState(Object navigationParameter, Dictionary<String, Object> pageState)
        {
        }

        /// <summary>
        /// Preserves state associated with this page in case the application is suspended or the
        /// page is discarded from the navigation cache.  Values must conform to the serialization
        /// requirements of <see cref="SuspensionManager.SessionState"/>.
        /// </summary>
        /// <param name="pageState">An empty dictionary to be populated with serializable state.</param>
        protected override void SaveState(Dictionary<String, Object> pageState)
        {
        }

        private async void pdfThumbnail_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var manager = new MagazineManager("http://librelio-europe.s3.amazonaws.com/niveales/wind/", "Magazines");

                await manager.LoadPLISTAsync();
                // In order to test this scenario, the WindowsStoreProxy.xml needs to be changed
                // The receipt provided by the CurrentAppSimulator is NOT signed so we cannot verify it
                
                //var receipt1 = await CurrentAppSimulator.RequestProductPurchaseAsync("1234", true);

                //var xmlDocument = new XmlDocument();
                //xmlDocument.LoadXml(receipt1);

                //XmlNodeList elemList1 = xmlDocument.GetElementsByTagName("Receipt");
                //var certID1 = elemList1[0].Attributes.GetNamedItem("CertificateId").NodeValue;

                // We'll use the sample receipt from http://msdn.microsoft.com/en-us/library/windows/apps/jj649137.aspx

                //string str = "";
                //var file = await KnownFolders.DocumentsLibrary.GetFileAsync("receipt.pmd");
                //using (var stream = await file.OpenAsync(FileAccessMode.Read))
                //{
                //    var dataReader = new DataReader(stream.GetInputStreamAt(0));

                //    dataReader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                //    var size = await dataReader.LoadAsync((uint)stream.Size);
                //    var receipt = dataReader.ReadString(size);

                //    receipt = Uri.EscapeDataString(receipt);
                //    var url = "http://54.244.231.175/test/win8_verify.php/?receipt=" + receipt;// +"&id=" + certID;
                //    //var url = "https://lic.apps.microsoft.com/licensing/certificateserver/?cid=b809e47cd0110a4db043b3f73e83acd917fe1336";
                //    str = await new HttpClient().GetAsync(url).Result.Content.ReadAsStringAsync();
                //}

                //file = await KnownFolders.DocumentsLibrary.CreateFileAsync("output.pmd", CreationCollisionOption.ReplaceExisting);
                //using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                //{
                //    var dataWriter = new DataWriter(stream.GetOutputStreamAt(0));
                //    dataWriter.WriteString(str);

                //    await dataWriter.StoreAsync();
                //    await dataWriter.FlushAsync();
                //}

                var metadataFile = await CreateMetadataFile("AntoineAlbeau");

                var sampleFile = await folder.CreateFileAsync("AntoineAlbeau.pdf", CreationCollisionOption.ReplaceExisting);

                statusText.Text = "Downloading pdf ... ";
                var progressIndicator = new Progress<int>((value) => progressBar.Value = value);
                cts = new CancellationTokenSource();

                var url = "http://librelio-europe.s3.amazonaws.com/niveales/wind/windfree_albeau2012/AntoineAlbeau.pdf";
                var stream = await DownloadFileAsyncWithProgress(url, sampleFile, progressIndicator, cts.Token);
                await GetUrlsFromPDF(stream);

                statusText.Text = "Downloading assets ... ";
                await DownloadAssetsAsync(metadataFile);

                statusText.Text = "Done";

                await Task.Delay(1000);

                this.Frame.Navigate(typeof(PdfViewPage), stream);
            }
            catch (HttpRequestException)
            {
            }
        }

        private async Task<StorageFile> CreateMetadataFile(string name)
        {
            //var roamingFolder = Windows.Storage.ApplicationData.Current.RoamingFolder;
            var roamingFolder = KnownFolders.DocumentsLibrary;
            var file = await roamingFolder.CreateFileAsync(name + ".pmd", CreationCollisionOption.ReplaceExisting);
            folder = await roamingFolder.CreateFolderAsync(name, CreationCollisionOption.GenerateUniqueName);
            var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);

            using (var outputStream = stream.GetOutputStreamAt(0))
            using (var dataWriter = new DataWriter(outputStream))
            {
                string data = name + "\r\n" + DateTime.Today.Month.ToString() + "/" + DateTime.Today.Day.ToString() + "/" + DateTime.Today.Year.ToString() + "\r\n";

                dataWriter.WriteString(data);

                await dataWriter.StoreAsync();
                await outputStream.FlushAsync();
            }

            return file;
        }

        private async Task<IRandomAccessStream> DownloadFileAsyncWithProgress(string url, StorageFile pdfFile, IProgress<int> progress = null, CancellationToken cancelToken = default(CancellationToken)) 
        {
            HttpClient client = new HttpClient();

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url); 

            int read = 0; 
            int offset = 0;
            byte[] responseBuffer = new byte[1024];

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancelToken);
            var length = response.Content.Headers.ContentLength;

            cancelToken.ThrowIfCancellationRequested();

            var stream = new InMemoryRandomAccessStream();

            using (var responseStream = await response.Content.ReadAsStreamAsync())
            {
                do
                {
                    cancelToken.ThrowIfCancellationRequested();

                    read = await responseStream.ReadAsync(responseBuffer, 0, responseBuffer.Length);

                    cancelToken.ThrowIfCancellationRequested();

                    await stream.AsStream().WriteAsync(responseBuffer, 0, read);

                    offset += read;
                    int val = (int)(offset * 100 / length);
                    progress.Report(val);
                }
                while (read != 0);
            }

            await stream.FlushAsync();

            using (var protectedStream = await ProtectPDFStream(stream))
            using (var fileStream = await pdfFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
            //using (var unprotectedStream = await UnprotectPDFStream(protectedStream))
            {

                await RandomAccessStream.CopyAsync(protectedStream, fileStream.GetOutputStreamAt(0));

                await fileStream.FlushAsync();
            }

            return stream;
        }

        private async Task GetUrlsFromPDF(IRandomAccessStream stream)
        {
            using (var dataReader = new DataReader(stream.GetInputStreamAt(0)))
            {
                uint u = await dataReader.LoadAsync((uint)stream.Size);
                IBuffer buffer = dataReader.ReadBuffer(u);

                GetPDFLinks(buffer);

                TimeSpan t = new TimeSpan(0, 0, 1);
                await Task.Delay(t);
            }
        }

        private void GetPDFLinks(IBuffer buffer)
        {
            var document = Document.Create(
                        buffer, // - file
                        DocumentType.PDF, // type
                        72 // - dpi
                      );

            var linkVistor = new LinkInfoVisitor();
            linkVistor.OnURILink += linkVistor_OnURILink;

            for (int i = 0; i < document.PageCount; i++)
            {
                var links = document.GetLinks(i);

                for (int j = 0; j < links.Count; j++)
                {
                    links[j].AcceptVisitor(linkVistor);
                    
                }
            }
        }

        void linkVistor_OnURILink(LinkInfoVisitor __param0, LinkInfoURI __param1)
        {
            string str = __param1.URI;
            if (str.Contains("localhost"))
            {
                links.Add(str);
            }
        }

        private async Task DownloadAssetsAsync(StorageFile metadataFile)
        {
            var absLinks = new List<string>();

            foreach (var link in links)
            {
                string absLink = link;
                var pos = absLink.IndexOf('?');
                if (pos >= 0) absLink = link.Substring(0, pos);
                string fileName = absLink.Replace("http://localhost/", "");
                string linkString = "";
                linkString = folder.Path + "\\" + absLink.Replace("http://localhost/", "") + "\r\n";
                absLink = absLink.Replace("http://localhost/", "http://librelio-europe.s3.amazonaws.com/niveales/wind/windfree_albeau2012/");
                absLinks.Add(absLink);

                var progressIndicator = new Progress<int>((value) => progressBar.Value = value);
                cts = new CancellationTokenSource();

                var sampleFile = await folder.CreateFileAsync(fileName + ".pmd", CreationCollisionOption.ReplaceExisting);

                await DownloadFileAsyncWithProgress(absLink, sampleFile, progressIndicator, cts.Token);

                using (var fileStream = await metadataFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
                using (var dataReader = new DataReader(fileStream.GetInputStreamAt(0)))
                using (var dataWriter = new DataWriter(fileStream.GetOutputStreamAt(0)))
                {
                    var len = await dataReader.LoadAsync((uint)fileStream.Size);
                    var data = dataReader.ReadString((uint)len);
                    var size = dataWriter.WriteString(data + linkString);
                    await fileStream.FlushAsync();
                    await dataWriter.StoreAsync();
                }
            }
        }

        private async Task<IRandomAccessStream> ProtectPDFStream(IRandomAccessStream source)
        {
            // Create a DataProtectionProvider object for the specified descriptor.
            DataProtectionProvider Provider = new DataProtectionProvider("LOCAL=user");

            InMemoryRandomAccessStream protectedData = new InMemoryRandomAccessStream();
            IOutputStream dest = protectedData.GetOutputStreamAt(0);

            await Provider.ProtectStreamAsync(source.GetInputStreamAt(0), dest);
            await dest.FlushAsync();

            //Verify that the protected data does not match the original
            DataReader reader1 = new DataReader(source.GetInputStreamAt(0));
            DataReader reader2 = new DataReader(protectedData.GetInputStreamAt(0));
            var size1 = await reader1.LoadAsync((uint)(source.Size < 10000 ? source.Size : 10000));
            var size2 = await reader2.LoadAsync((uint)(protectedData.Size < 10000 ? protectedData.Size : 10000));
            IBuffer buffOriginalData = reader1.ReadBuffer((uint)size1);
            IBuffer buffProtectedData = reader2.ReadBuffer((uint)size2);

            if (CryptographicBuffer.Compare(buffOriginalData, buffProtectedData))
            {
                throw new Exception("ProtectPDFStream returned unprotected data");
            }

            // Return the encrypted data.
            return protectedData;
        }

        private async Task<IRandomAccessStream> UnprotectPDFStream(IRandomAccessStream source)
        {
            // Create a DataProtectionProvider object.
            DataProtectionProvider Provider = new DataProtectionProvider();

            InMemoryRandomAccessStream unprotectedData = new InMemoryRandomAccessStream();
            IOutputStream dest = unprotectedData.GetOutputStreamAt(0);

            await Provider.UnprotectStreamAsync(source.GetInputStreamAt(0), dest);
            await unprotectedData.FlushAsync();

            return unprotectedData;
        }
    }
}
