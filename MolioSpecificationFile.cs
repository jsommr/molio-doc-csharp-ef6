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
    public class MolioSpecificationFile : DbContext
    {
        public DbSet<WorkSpecification> WorkSpecifications { get; set; }

        public DbSet<WorkSpecificationSection> WorkSpecificationSections { get; set; }

        public DbSet<ConstructionElementSpecificationSection> ConstructionElementSpecificationSections { get; set; }

        public DbSet<ConstructionElementSpecification> ConstructionElementSpecifications { get; set; }

        public DbSet<Attachment> Attachments { get; set; }

        public DbSet<CustomData> CustomData { get; set; }

        /// <param name="contextOwnsConnection">If set to true the connection is disposed when the context is disposed, otherwise the caller must dispose the connection.</param>
        public MolioSpecificationFile(DbConnection connection, bool contextOwnsConnection)
            : base(connection, contextOwnsConnection) { }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Conventions.Remove<PluralizingTableNameConvention>();
        }
    }

    public interface ISection
    {
        int SectionNo { get; set; }

        string Heading { get; set; }

        string Body { get; set; }

        Guid MolioSectionGuid { get; set; }

        int? ParentId { get; set; }
    }

    [Table("work_specification")]
    public partial class WorkSpecification
    {
        [Key, Column("work_specification_id")]
        public int WorkSpecificationId { get; set; }

        [Column("work_area_name")]
        public string WorkAreaName { get; set; }

        [Column("work_area_code")]
        public string WorkAreaCode { get; set; }

        public Guid Key { get; set; }

        public List<WorkSpecificationSection> Sections { get; set; } = new List<WorkSpecificationSection>();
    }

    [Table("work_specification_section")]
    public partial class WorkSpecificationSection : ISection
    {
        [Key, Column("work_specification_section_id")]
        public int WorkSpecificationSectionId { get; set; }

        [Column("work_specification_id")]
        public int WorkSpecificationId { get; set; }

        [Column("section_no")]
        public int SectionNo { get; set; }

        public string Heading { get; set; }

        public string Body { get; set; } = "";

        [Column("molio_section_guid")]
        public Guid MolioSectionGuid { get; set; }

        [Column("parent_id")]
        public int? ParentId { get; set; }

        [ForeignKey(nameof(ParentId))]
        public WorkSpecificationSection Parent { get; set; }

        public List<WorkSpecificationSection> Sections { get; set; } = new List<WorkSpecificationSection>();

        public List<WorkSpecificationSectionConstructionElementSpecification> WorkSpecificationSectionConstructionElementSpecifications { get; set; }
            = new List<WorkSpecificationSectionConstructionElementSpecification>();
    }

    [Table("work_specification_section_construction_element_specification")]
    public class WorkSpecificationSectionConstructionElementSpecification
    {
        [Key, Column("work_specification_section_construction_element_specification_id")]
        public int ArbejdsbeskrivelseSectionBygningsdelsbeskrivelseId { get; set; }

        [Column("work_specification_section_id")]
        public int WorkSpecificationSectionId { get; set; }

        [Column("construction_element_specification_id")]
        public int ConstructionElementSpecificationId { get; set; }
    }
   

    [Table("construction_element_specification")]
    public partial class ConstructionElementSpecification
    {
        [Key, Column("construction_element_specification_id")]
        public int ConstructionElementSpecificationId { get; set; }

        [Column("molio_specification_guid")]
        public Guid MolioSpecificationGuid { get; set; }

        public string Title { get; set; }

        public List<ConstructionElementSpecificationSection> Sections { get; set; } = new List<ConstructionElementSpecificationSection>();
    }

    [Table("construction_element_specification_section")]
    public partial class ConstructionElementSpecificationSection : ISection
    {
        [Key, Column("construction_element_specification_section_id")]
        public int ConstructionElementSpecificationSectionId { get; set; }

        [Column("construction_element_specification_id")]
        public int ConstructionElementSpecificationId { get; set; }

        public ConstructionElementSpecification ConstructionElementSpecification { get; set; }

        [Column("section_no")]
        public int SectionNo { get; set; }

        public string Heading { get; set; }

        public string Body { get; set; } = "";

        [Column("molio_section_guid")]
        public Guid MolioSectionGuid { get; set; }

        [Column("parent_id")]
        public int? ParentId { get; set; }

        [ForeignKey(nameof(ParentId))]
        public ConstructionElementSpecificationSection Parent { get; set; }

        public List<ConstructionElementSpecificationSection> Sections { get; set; } = new List<ConstructionElementSpecificationSection>();
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

    [Table("custom_data")]
    public class CustomData
    {
        [Key]
        public string Key { get; set; }

        public byte[] Value { get; set; }

        public CustomData(string key, byte[] value)
        {
            Key = key;
            Value = value;
        }
    }
}
