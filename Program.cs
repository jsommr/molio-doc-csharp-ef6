using AngleSharp;
using AngleSharp.Html.Parser;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web;

namespace MolioDocEF6
{
    class Program
    {
        static HttpClient http = new HttpClient();

        static void Main(string[] args)
        {
            SQLiteEF6Fix.Initialise();

            MainAsync().GetAwaiter().GetResult();

            Console.WriteLine("Done");
            Console.ReadKey();
        }

        static async Task MainAsync()
        {
            var outFilePath = Path.Combine(AppContext.BaseDirectory, "molio.mspec");

            // It's unfortunate the database must be placed physically on disc and can't just reside in
            // memory. Using "Data Source=:memory:" as connection string makes no difference - there's
            // no way to extract the memory stream.
            var dbFilePath = Path.GetTempFileName();

            try
            {
                using (var doc = new MolioDoc(BlankDatabase(dbFilePath), contextOwnsConnection: true))
                {
                    await WriteDoc(doc);
                    await EmbedImages(doc, doc.BygningsdelsbeskrivelseSections);
                    await EmbedImages(doc, doc.BasisbeskrivelseSections);
                    await EmbedImages(doc, doc.VejledningSections);
                }

                GZipDoc(dbFilePath, outFilePath);
            }
            finally
            {
                File.Delete(dbFilePath);
            }
        }

        static async Task WriteDoc(MolioDoc doc)
        {
            var bygningsdelsbeskrivelse = new Bygningsdelsbeskrivelse
            {
                Name = "Test",
                BygningsdelsbeskrivelseGuid = Guid.NewGuid(),
                BasisbeskrivelseVersionGuid = Guid.NewGuid()
            };

            doc.Bygningsdelsbeskrivelser.Add(bygningsdelsbeskrivelse);

            var omfang =
                new BygningsdelsbeskrivelseSection(1, "OMFANG", "Lorem ipsum <img src='https://via.placeholder.com/200C/O' />");

            var almeneSpecifikationer =
                new BygningsdelsbeskrivelseSection(2, "ALMENE SPECIFIKATIONER");
            var generelt =
                new BygningsdelsbeskrivelseSection(almeneSpecifikationer, 1, "Generelt", "Noget tekst")
                {
                    MolioSectionGuid = Guid.NewGuid()
                };
            var thirdLevelSection =
                new BygningsdelsbeskrivelseSection(generelt, 5, "Tredje niveau", "Lorem ipsum <img src='https://via.placeholder.com/150C/O' /> hey yo <img src='https://via.placeholder.com/150C/O' />");

            var referenceliste = Attachment.Json("referenceliste.json", "{ \"test\": 1 }");
            thirdLevelSection.Attach(referenceliste);

            using (var samplePdf = GetSamplePdf())
                thirdLevelSection.Attach(Attachment.Pdf("basisbeskrivelse.pdf", samplePdf));

            bygningsdelsbeskrivelse.Sections.AddRange(new[] {
                omfang, almeneSpecifikationer, generelt, thirdLevelSection
            });

            bygningsdelsbeskrivelse.Sections.Add(omfang);

            await doc.SaveChangesAsync();
        }

        /// <summary>
        /// Downloads all external images for embedding. img src's are changed to a urn pointing to their internal location.
        /// The images are stored as an `attachment` and linked to the section, so if the section is removed, there might no
        /// longer be any use for the image, and it can be removed.
        /// </summary>
        async static Task EmbedImages<T>(MolioDoc doc, IEnumerable<ISection<T>> sections)
        {
            var supportedImageMimeTypes = new[]
            {
                "image/apng",
                "image/bmp",
                "image/gif",
                "image/jpeg",
                "image/png",
                "image/svg+xml",
                "image/webp"
            };
            using (var browsingContext = BrowsingContext.New())
            {
                var htmlParser = browsingContext.GetService<IHtmlParser>();

                foreach (var section in sections)
                {
                    var html = htmlParser.ParseDocument(section.Text);

                    foreach (var imageElement in html.Images)
                    {
                        // If the file have been written once, some images might already be embedded.
                        // We can safely ignore those
                        if (imageElement.Source.StartsWith("urn:"))
                            continue;

                        // Relative (and invalid) url's are not supported
                        if (!Uri.IsWellFormedUriString(imageElement.Source, UriKind.Absolute))
                            throw new Exception($"Invalid image source '{imageElement.Source}'. Please use absolute url's.");

                        var imageUri = new Uri(imageElement.Source);

                        var imageResponse = await http.GetAsync(imageElement.Source);
                        imageResponse.EnsureSuccessStatusCode();

                        // Get the mime type from headers or file name
                        var mimeType =
                            imageResponse.Content.Headers.ContentType?.MediaType ??
                            MimeMapping.GetMimeMapping(Path.GetFileName(imageUri.LocalPath));

                        if (mimeType == null)
                            throw new Exception($"Unable to determine mime type for image source '{imageElement.Source}'");

                        if (!supportedImageMimeTypes.Contains(mimeType))
                            throw new Exception($"Unsupported mime type '{mimeType}' for image source '{imageElement.Source}'");

                        var imageBytes = await imageResponse.Content.ReadAsByteArrayAsync();

                        // Calculate the hash to avoid embedding this image twice
                        var imageHash = CalculateSHA1Hash(imageBytes);

                        var attachment = doc.Attachments.FirstOrDefault(a => a.Hash == imageHash);

                        if (attachment == null)
                        {
                            attachment = section.Attach(new Attachment
                            {
                                Name = Path.GetFileName(imageUri.LocalPath),
                                MimeType = mimeType,
                                Content = imageBytes,
                                Hash = imageHash
                            });

                            await doc.SaveChangesAsync(); // Assigns an id to attachment
                        }

                        imageElement.Source = "urn:molio:specification:attachment:" + attachment.AttachmentId;
                    }

                    section.Text = html.Body.InnerHtml;

                    await doc.SaveChangesAsync();
                }
            }
        }

        static void GZipDoc(string dbFilePath, string outFilePath)
        {
            using (var dbFileHandle = File.OpenRead(dbFilePath))
            using (var outFileHandle = File.Open(outFilePath, FileMode.Create, FileAccess.Write))
            using (var gzip = new GZipStream(outFileHandle, CompressionMode.Compress))
                dbFileHandle.CopyTo(gzip);
        }

        static SQLiteConnection BlankDatabase(string dbFilePath)
        {
            var sqlite = new SQLiteConnection("Data Source=" + dbFilePath);
            sqlite.Open();
            using (var template = new SQLiteCommand(GetSqlTemplate(), sqlite))
                template.ExecuteNonQuery();
            return sqlite;
        }

        static string GetSqlTemplate()
        {
            using (var template = Assembly.GetExecutingAssembly().GetManifestResourceStream("MolioDocEF6.Template.sql"))
            using (var reader = new StreamReader(template))
                return reader.ReadToEnd();
        }

        static Stream GetSamplePdf() => Assembly.GetExecutingAssembly().GetManifestResourceStream("MolioDocEF6.Sample.pdf");

        static byte[] CalculateSHA1Hash(byte[] data)
        {
            using (var sha1 = new SHA1CryptoServiceProvider())
                return sha1.ComputeHash(data);
        }
    }
}
