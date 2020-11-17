using AngleSharp;
using AngleSharp.Html.Parser;
using Jose;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace MolioDocEF6
{
    class Program
    {
        static HttpClient http = new HttpClient();

        static void Main(string[] args)
        {
            SQLiteEF6Fix.Initialize();

            MainAsync().GetAwaiter().GetResult();

            Console.WriteLine("Done");
            Console.ReadKey();
        }

        static async Task MainAsync()
        {
            var outFilePath = Path.Combine(AppContext.BaseDirectory, "molio.mspec.gz");

            // It's unfortunate the database must be placed physically on disc and can't just reside in
            // memory. Using "Data Source=:memory:" as connection string makes no difference - there's
            // no way to extract the memory stream.
            var dbFilePath = Path.GetTempFileName();

            try
            {
                using (var file = new MolioSpecificationFile(BlankDatabase(dbFilePath), contextOwnsConnection: true))
                {
                    var specTool = await InitSpecToolClient("http://test.bips.spec.insilico.dk");
                    var workAreaDocuments = await GetWorkAreaDocuments(specTool, "S236.01 Gulve trae og laminat");

                    var specToolConstructionElementSpecificationRefs = workAreaDocuments.Where(doc => doc.Type == "Bygningsdelsbeskrivelse");
                    foreach (var specToolConstructionElementSpecificationRef in specToolConstructionElementSpecificationRefs)
                    {
                        var specToolConstructionElementSpecification = await specTool.GetDocument(specToolConstructionElementSpecificationRef.Id);
                        await WriteConstructionElementSpecification(file, specToolConstructionElementSpecification);
                    }

                    var specToolWorkSpecificationRef = workAreaDocuments.First(doc => doc.Type == "Arbejdsbeskrivelse");
                    var specToolWorkSpecification = await specTool.GetDocument(specToolWorkSpecificationRef.Id);
                    var workSpecification = await WriteWorkSpecification(
                        file,
                        specToolWorkSpecification,
                        specToolWorkSpecificationRef.WorkAreaName,
                        specToolWorkSpecificationRef.WorkAreaId);

                    // Workaround to have the old specification format map to the new one. This associates all construction element specifications
                    // with the first work specification section "1. OMFANG".
                    workSpecification.Sections
                        .First(s => s.SectionNo == 1 && s.Parent == null)
                        .WorkSpecificationSectionConstructionElementSpecifications =
                            file.ConstructionElementSpecifications.ToList()
                            .Select(ces =>
                                new WorkSpecificationSectionConstructionElementSpecification
                                {
                                    ConstructionElementSpecificationId = ces.ConstructionElementSpecificationId,
                                    WorkSpecificationSectionId = workSpecification.WorkSpecificationId
                                }).ToList();

                    // Write claims to custom data, to make this file a 'key' to allow suppliers to retrieve paid data from Molio
                    // TODO: Change secretKey
                    var secretKey = new byte[] { 164, 60, 194, 0, 161, 189, 41, 38, 130, 89, 141, 164, 45, 170, 159, 209, 69, 137, 243, 216, 191, 131, 47, 250, 32, 107, 231, 117, 37, 158, 225, 234 };
                    var claims = JWT.Encode(new { workAreaDocIds = workAreaDocuments.Select(d => d.Id) }, secretKey, JwsAlgorithm.HS256);
                    file.CustomData.Add(new CustomData("claims", Encoding.UTF8.GetBytes(claims)));

                    await file.SaveChangesAsync();

                    await EmbedImages(file, file.ConstructionElementSpecificationSections);
                    await EmbedImages(file, file.WorkSpecificationSections);
                }

                GZipDoc(dbFilePath, outFilePath);
            }
            finally
            {
                File.Delete(dbFilePath);
            }
        }

        static async Task<SpecToolClient> InitSpecToolClient(string url)
        {
            var credentials = Environment.GetEnvironmentVariable("SPECTOOL_CREDENTIALS", EnvironmentVariableTarget.User).Split(':');
            var username = credentials[0];
            var password = credentials[1];
            return await SpecToolClient.Login(new Uri(url), username, password);
        }

        static async Task<IEnumerable<SpecToolDocument>> GetWorkAreaDocuments(SpecToolClient specTool, string workAreaName)
        {
            var workAreas = await specTool.GetWorkAreas();
            var workArea = workAreas.FirstOrDefault(wa => wa.Name == workAreaName);
            return workArea == null
                ? Enumerable.Empty<SpecToolDocument>()
                : await specTool.GetDocuments(workArea.Id);
        }

        static async Task<WorkSpecification> WriteWorkSpecification(MolioSpecificationFile file, SpecToolDocument specToolDoc, string workAreaName, string workAreaId)
        {
            var specification = file.WorkSpecifications.Add(new WorkSpecification
            {
                WorkAreaName = workAreaName,
                WorkAreaCode = workAreaId,
                Key = Guid.NewGuid()
            });

            specification.Sections = specToolDoc.Sections
                 .Select(s => SpecToolSectionToWorkSpecificationSection(s))
                 .ToList();

            await file.SaveChangesAsync();

            return specification;
        }

        static WorkSpecificationSection SpecToolSectionToWorkSpecificationSection(SpecToolSection specToolSection, WorkSpecificationSection parent = null)
        {
            var section = new WorkSpecificationSection
            {
                MolioSectionGuid = specToolSection.Id,
                Heading = specToolSection.Title,
                SectionNo = int.Parse(specToolSection.Number.Trim('.').Split('.').Last()),
                Body = string.Join("\n", specToolSection.Groups.FirstOrDefault(g => g.Name == "Projektspecifik beskrivelse")?.Contents.Select(c => c.MasterText))
            };

            section.Sections = specToolSection.Sections
                .Select(s => SpecToolSectionToWorkSpecificationSection(s, section))
                .ToList();

            return section;
        }

        static async Task<ConstructionElementSpecification> WriteConstructionElementSpecification(MolioSpecificationFile file, SpecToolDocument specToolDoc)
        {
            var specification = file.ConstructionElementSpecifications.Add(new ConstructionElementSpecification
            {
                Title = specToolDoc.Name,
                MolioSpecificationGuid = specToolDoc.Id
            });

            specification.Sections = specToolDoc.Sections
                .Select(s => SpecSectionToConstructionElementSpecificationSection(s))
                .ToList();

            await file.SaveChangesAsync();

            return specification;
        }

        static ConstructionElementSpecificationSection SpecSectionToConstructionElementSpecificationSection(SpecToolSection specToolSection, ConstructionElementSpecificationSection parent = null)
        {
            var section = new ConstructionElementSpecificationSection
            {
                MolioSectionGuid = specToolSection.Id,
                Heading = specToolSection.Title,
                SectionNo = int.Parse(specToolSection.Number.Trim('.').Split('.').Last())
            };

            section.Sections = specToolSection.Sections
                .Select(s => SpecSectionToConstructionElementSpecificationSection(s, section))
                .ToList();

            return section;
        }

        /// <summary>
        /// Downloads all external images for embedding. img src's are changed to a urn pointing to their internal location.
        /// The images are stored as an `attachment`.
        /// </summary>
        async static Task EmbedImages(MolioSpecificationFile file, IEnumerable<ISection> sections)
        {
            var supportedMimeTypes = new[]
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
                    var html = htmlParser.ParseDocument(section.Body);

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

                        if (!supportedMimeTypes.Contains(mimeType))
                            throw new Exception($"Unsupported mime type '{mimeType}' for image source '{imageElement.Source}'");

                        var imageBytes = await imageResponse.Content.ReadAsByteArrayAsync();

                        // Compute hash to avoid embedding this image twice
                        var imageHash = ComputeSHA1Hash(imageBytes);

                        var attachment = file.Attachments.FirstOrDefault(a => a.Hash == imageHash);

                        if (attachment == null)
                        {
                            attachment = file.Attachments.Add(new Attachment
                            {
                                Name = Path.GetFileName(imageUri.LocalPath),
                                MimeType = mimeType,
                                Content = imageBytes,
                                Hash = imageHash
                            });

                            await file.SaveChangesAsync(); // Assigns an id to attachment
                        }

                        imageElement.Source = "urn:mspec:attachment:" + attachment.AttachmentId;
                    }

                    section.Body = html.Body.InnerHtml;

                    await file.SaveChangesAsync();
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

        static byte[] ComputeSHA1Hash(byte[] data)
        {
            using (var sha1 = new SHA1CryptoServiceProvider())
                return sha1.ComputeHash(data);
        }
    }
}
