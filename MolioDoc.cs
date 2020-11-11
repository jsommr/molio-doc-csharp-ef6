using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.ModelConfiguration.Conventions;
using System.IO;
using System.Text;

namespace MolioDocEF6
{
    public class MolioDoc : DbContext
    {
        public DbSet<Vejledning> Vejledninger { get; set; }

        public DbSet<VejledningSection> VejledningSections { get; set; }

        public DbSet<Basisbeskrivelse> Basisbeskrivelser { get; set; }

        public DbSet<BasisbeskrivelseSection> BasisbeskrivelseSections { get; set; }

        public DbSet<BygningsdelsbeskrivelseSection> BygningsdelsbeskrivelseSections { get; set; }

        public DbSet<Bygningsdelsbeskrivelse> Bygningsdelsbeskrivelser { get; set; }

        public DbSet<Attachment> Attachments { get; set; }

        /// <param name="contextOwnsConnection">If set to true the connection is disposed when the context is disposed, otherwise the caller must dispose the connection.</param>
        public MolioDoc(DbConnection connection, bool contextOwnsConnection)
            : base(connection, contextOwnsConnection) { }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Conventions.Remove<PluralizingTableNameConvention>();
        }
    }

    public interface ISection<TEntity>
    {
        int SectionNo { get; set; }

        string Heading { get; set; }

        string Text { get; set; }

        Guid MolioSectionGuid { get; set; }

        int? ParentId { get; set; }

        TEntity Parent { get; set; }

        Attachment Attach(Attachment attachment);
    }

    public interface IAttachmentRelationship<in TEntity>
    {
        int AttachmentId { get; set; }
    }

    public partial class Vejledning
    {
        [Key, Column("vejledning_id")]
        public int VejledningId { get; set; }

        public string Name { get; set; }
    }

    [Table("vejledning_section")]
    public partial class VejledningSection : ISection<VejledningSection>
    {
        [Key, Column("vejledning_section_id")]
        public int VejledningSectionId { get; set; }

        [Column("vejledning_id")]
        public int VejledningId { get; set; }

        [Column("section_no")]
        public int SectionNo { get; set; }

        public string Heading { get; set; }

        public string Text { get; set; } = "";

        [Column("molio_section_guid")]
        public Guid MolioSectionGuid { get; set; }

        [Column("parent_id")]
        public int? ParentId { get; set; }

        [ForeignKey(nameof(ParentId))]
        public VejledningSection Parent { get; set; }

        public List<VejledningSectionAttachment> Attachments { get; set; } = new List<VejledningSectionAttachment>();

        public Attachment Attach(Attachment attachment)
        {
            Attachments.Add(new VejledningSectionAttachment { Attachment = attachment });
            return attachment;
        }
    }

    [Table("vejledning_section_attachment")]
    public partial class VejledningSectionAttachment : IAttachmentRelationship<VejledningSection>
    {
        [Key, Column("vejledning_section_attachment_id")]
        public int VejledningSectionAttachmentId { get; set; }

        [Column("attachment_id")]
        public int AttachmentId { get; set; }

        public Attachment Attachment { get; set; }

        [Column("vejledning_section_id")]
        public int VejledningSectionId { get; set; }
        public VejledningSection VejledningSection { get; set; }
    }

    public partial class Basisbeskrivelse
    {
        [Key, Column("basisbeskrivelse_id")]
        public int BasisbeskrivelseId { get; set; }

        public string Name { get; set; }
    }

    [Table("basisbeskrivelse_section")]
    public partial class BasisbeskrivelseSection : ISection<BasisbeskrivelseSection>
    {
        [Key, Column("basisbeskrivelse_section_id")]
        public int BasisbeskrivelseSectionId { get; set; }

        [Column("basisbeskrivelse_id")]
        public int BasisbeskrivelseId { get; set; }

        [Column("section_no")]
        public int SectionNo { get; set; }

        public string Heading { get; set; }

        public string Text { get; set; } = "";

        [Column("molio_section_guid")]
        public Guid MolioSectionGuid { get; set; }

        [Column("parent_id")]
        public int? ParentId { get; set; }

        [ForeignKey(nameof(ParentId))]
        public BasisbeskrivelseSection Parent { get; set; }

        public List<BasisbeskrivelseSectionAttachment> Attachments { get; set; } = new List<BasisbeskrivelseSectionAttachment>();

        public Attachment Attach(Attachment attachment)
        {
            Attachments.Add(new BasisbeskrivelseSectionAttachment { Attachment = attachment });
            return attachment;
        }
    }

    [Table("basisbeskrivelse_section_attachment")]
    public partial class BasisbeskrivelseSectionAttachment : IAttachmentRelationship<BasisbeskrivelseSectionAttachment>
    {
        [Key, Column("basisbeskrivelse_section_attachment_id")]
        public int BasisbeskrivelseSectionAttachmentId { get; set; }

        [Column("attachment_id")]
        public int AttachmentId { get; set; }

        public Attachment Attachment { get; set; }

        [Column("basisbeskrivelse_section_id")]
        public int BasisbeskrivelseSectionId { get; set; }
        public BasisbeskrivelseSection BasisbeskrivelseSection { get; set; }
    }

    [Table("bygningsdelsbeskrivelse_section")]
    public partial class BygningsdelsbeskrivelseSection : ISection<BygningsdelsbeskrivelseSection>
    {
        [Key, Column("bygningsdelsbeskrivelse_section_id")]
        public int BygningsdelsbeskrivelseSectionId { get; set; }

        [Column("bygningsdelsbeskrivelse_id")]
        public int BygningsdelsbeskrivelseId { get; set; }

        public Bygningsdelsbeskrivelse Bygningsdelsbeskrivelse { get; set; }

        [Column("section_no")]
        public int SectionNo { get; set; }

        public string Heading { get; set; }

        public string Text { get; set; } = "";

        [Column("molio_section_guid")]
        public Guid MolioSectionGuid { get; set; }

        [Column("parent_id")]
        public int? ParentId { get; set; }

        [ForeignKey(nameof(ParentId))]
        public BygningsdelsbeskrivelseSection Parent { get; set; }

        public List<BygningsdelsbeskrivelseSectionAttachment> Attachments { get; set; } = new List<BygningsdelsbeskrivelseSectionAttachment>();

        public BygningsdelsbeskrivelseSection() { }

        public BygningsdelsbeskrivelseSection(int sectionNo, string heading, string text = "")
        {
            SectionNo = sectionNo;
            Heading = heading;
            Text = text;
        }

        public BygningsdelsbeskrivelseSection(BygningsdelsbeskrivelseSection parent, int sectionNo, string heading, string text = "")
            : this(sectionNo, heading, text)
        {
            Parent = parent;
        }

        public Attachment Attach(Attachment attachment)
        {
            Attachments.Add(new BygningsdelsbeskrivelseSectionAttachment { Attachment = attachment });
            return attachment;
        }
    }

    [Table("bygningsdelsbeskrivelse_section_attachment")]
    public partial class BygningsdelsbeskrivelseSectionAttachment : IAttachmentRelationship<BygningsdelsbeskrivelseSectionAttachment>
    {
        [Key, Column("bygningsdelsbeskrivelse_section_attachment_id")]
        public int BygningsdelsbeskrivelseSectionAttachmentId { get; set; }

        [Column("attachment_id")]
        public int AttachmentId { get; set; }

        public Attachment Attachment { get; set; }

        [Column("bygningsdelsbeskrivelse_section_id")]
        public int BygningsdelsbeskrivelseSectionId { get; set; }
        public BygningsdelsbeskrivelseSection BygningsdelsbeskrivelseSection { get; set; }
    }

    [Table("Bygningsdelsbeskrivelse")]
    public partial class Bygningsdelsbeskrivelse
    {
        [Key, Column("bygningsdelsbeskrivelse_id")]
        public int BygningsdelsbeskrivelseId { get; set; }

        [Column("bygningsdelsbeskrivelse_guid")]
        public Guid BygningsdelsbeskrivelseGuid { get; set; }

        public string Name { get; set; }

        [Column("basisbeskrivelse_version_guid")]
        public Guid BasisbeskrivelseVersionGuid { get; set; }

        public List<BygningsdelsbeskrivelseSection> Sections { get; set; } = new List<BygningsdelsbeskrivelseSection>();
    }

    [Table("attachment")]
    public partial class Attachment
    {
        [Key, Column("attachment_id")]
        public int AttachmentId { get; set; }

        public string Name { get; set; }

        [Column("mime_type")]
        public string MimeType { get; set; }

        public byte[] Content { get; set; }

        public byte[] Hash { get; set; }

        public static Attachment Json(string name, string content) =>
            new Attachment
            {
                Name = name,
                MimeType = "application/json",
                Content = Encoding.UTF8.GetBytes(content)
            };

        public static Attachment Pdf(string name, Stream stream) =>
            new Attachment
            {
                Name = name,
                MimeType = "application/pdf",
                Content = StreamToBytes(stream)
            };

        static byte[] StreamToBytes(Stream stream)
        {
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
    }
}
