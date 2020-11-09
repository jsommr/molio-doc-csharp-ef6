using System;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace MolioDocEF6
{
    class Program
    {
        static void Main(string[] args)
        {
            SQLiteEF6Fix.Initialise();

            var outFilePath = Path.Combine(AppContext.BaseDirectory, "molio.db.gz");

            // It's unfortunate the database must be placed physically on disc and can't just reside in
            // memory. Using "Data Source=:memory:" as connection string makes no difference - there's
            // no way to extract the memory stream.
            var dbFilePath = Path.GetTempFileName();

            try
            {
                using (var db = BlankDatabase(dbFilePath))
                using (var doc = new MolioDoc(db, contextOwnsConnection: false))
                    WriteDoc(doc);

                // Code above could also be
                // using (var doc = new MolioDoc(BlankDatabase(dbFilePath), contextOwnsConnection: true))
                //     WriteDoc(doc);

                GZipDoc(dbFilePath, outFilePath);
            }
            finally
            {
                File.Delete(dbFilePath);
            }

            Console.WriteLine("Done");
            Console.ReadKey();
        }

        static void WriteDoc(MolioDoc doc)
        {
            var bygningsdelsbeskrivelse = new Bygningsdelsbeskrivelse
            {
                Name = "Test",
                BygningsdelsbeskrivelseGuid = Guid.NewGuid(),
                BasisbeskrivelseVersionGuid = Guid.NewGuid()
            };

            doc.Bygningsdelsbeskrivelser.Add(bygningsdelsbeskrivelse);

            var omfang =
                new BygningsdelsbeskrivelseSection(1, "OMFANG");
            var almeneSpecifikationer =
                new BygningsdelsbeskrivelseSection(2, "ALMENE SPECIFIKATIONER");
            var generelt =
                new BygningsdelsbeskrivelseSection(almeneSpecifikationer, 1, "Generelt", "Noget tekst")
                {
                    MolioSectionGuid = Guid.NewGuid()
                };
            var thirdLevelSection =
                new BygningsdelsbeskrivelseSection(generelt, 5, "Tredje niveau", "Lorem ipsum");

            var referenceliste = Attachment.Json("referenceliste.json", "{ \"test\": 1 }");
            thirdLevelSection.Attach(referenceliste);

            using (var samplePdf = GetSamplePdf())
                thirdLevelSection.Attach(Attachment.Pdf("basisbeskrivelse.pdf", samplePdf));

            bygningsdelsbeskrivelse.Sections.AddRange(new[] {
                omfang, almeneSpecifikationer, generelt, thirdLevelSection
            });

            doc.SaveChanges();
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
    }
}
