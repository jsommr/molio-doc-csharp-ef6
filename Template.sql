-- Enfore foreign key constraints
pragma foreign_keys = on;

-- Document version. Incrementet when the schema changes.
pragma user_version = 1;

-------------------------------------------------------------------------------
-- Projekt
-------------------------------------------------------------------------------

create table projekt (
  projekt_id      integer primary key,
  projekt_guid    blob    not null,
  projekt_version integer not null,
  
  constraint "projekt_guid is not a valid guid"
    check (
      typeof(projekt_guid) = 'blob' and
      length(projekt_guid) = 16
    )
);

-------------------------------------------------------------------------------
-- Attachment
-------------------------------------------------------------------------------

create table attachment (
  attachment_id integer primary key,
  name          text    not null,
  mime_type     text    not null,
  content       blob    not null, -- Encode as UTF-8 if storing text

  constraint "content is not a blob" check (typeof(content) = 'blob')
);

-------------------------------------------------------------------------------
-- Bygningsdelsbeskrivelse
-------------------------------------------------------------------------------

create table bygningsdelsbeskrivelse (
  bygningsdelsbeskrivelse_id    integer primary key,
  name                          text    not null,

  -- Used for references outside of database, must be kept the same across exports
  bygningsdelsbeskrivelse_guid  blob    not null,
  basisbeskrivelse_version_guid blob    not null,

  constraint "bygningsdelsbeskrivelse_guid is not a valid guid"
    check (
      typeof(bygningsdelsbeskrivelse_guid) = 'blob' and
      length(bygningsdelsbeskrivelse_guid) = 16
    ),

  constraint "basisbeskrivelse_version_guid is not a valid guid"
    check (
      typeof(basisbeskrivelse_version_guid) = 'blob' and
      length(basisbeskrivelse_version_guid) = 16
    )
);

create table bygningsdelsbeskrivelse_section (
  bygningsdelsbeskrivelse_section_id integer primary key,
  bygningsdelsbeskrivelse_id         integer not null,
  section_no                         integer not null,
  heading                            text    not null,
  text                               text    not null default '',
  molio_section_guid                 blob,
  parent_id                          integer,

  foreign key (bygningsdelsbeskrivelse_id) references bygningsdelsbeskrivelse,
  foreign key (parent_id) references bygningsdelsbeskrivelse_section,

  constraint "molio_section_guid is not a valid guid"
    check (
      molio_section_guid is null or
      ( typeof(molio_section_guid) = 'blob' and
        length(molio_section_guid) = 16 ))
);

create table bygningsdelsbeskrivelse_section_attachment (
  bygningsdelsbeskrivelse_section_attachment_id integer primary key,
  bygningsdelsbeskrivelse_section_id            integer not null,
  attachment_id                                 integer not null,

  foreign key (bygningsdelsbeskrivelse_section_id)
  references bygningsdelsbeskrivelse_section,

  foreign key (attachment_id) references attachment,
  
  constraint "Same attachment cannot be referenced more than once for the same bygningsdelsbeskrivelse_section"
    unique (bygningsdelsbeskrivelse_section_id, attachment_id)
);

-- sqlite treats all null values as different, so a constraint like
-- unique (parent_id, section_no) won't do, because parent_id can be null.
create unique index bygningsdelsbeskrivelse_section_unique_idx
on bygningsdelsbeskrivelse_section (
  bygningsdelsbeskrivelse_id,
  ifnull(parent_id, -1),
  section_no
);

-------------------------------------------------------------------------------
-- Arbejdsbeskrivelse
-------------------------------------------------------------------------------

create table arbejdsbeskrivelse (
  arbejdsbeskrivelse_id integer primary key,
  work_area_code        text    not null,
  work_area_name        text    not null,

  -- Key is used to associate foreign data with an arbejdsbeskrivelse
  -- and must be the same for every export of this project
  key                   blob    not null,

  constraint "key is not a valid guid"
    check (
      key is null or
      ( typeof(key) = 'blob' and
        length(key) = 16 ))
);

create table arbejdsbeskrivelse_section (
  arbejdsbeskrivelse_section_id integer primary key,
  arbejdsbeskrivelse_id         integer not null,
  section_no                    int     not null,
  heading                       text    not null,
  text                          text    not null default '',
  molio_section_guid            blob,
  parent_id                     integer,

  foreign key (arbejdsbeskrivelse_id) references arbejdsbeskrivelse,
  foreign key (parent_id) references arbejdsbeskrivelse_section,

  constraint "molio_section_guid is not a valid guid"
    check (
      molio_section_guid is null or
      ( typeof(molio_section_guid) = 'blob' and
        length(molio_section_guid) = 16 ))
);

create table arbejdsbeskrivelse_section_bygningsdelsbeskrivelse (
  arbejdsbeskrivelse_section_bygningsdelsbeskrivelse_id integer primary key,
  arbejdsbeskrivelse_section_id                         integer not null,
  bygningsdelsbeskrivelse_id                            integer not null,

  foreign key (arbejdsbeskrivelse_section_id)
  references arbejdsbeskrivelse_section,
  
  foreign key (bygningsdelsbeskrivelse_id) references bygningsdelsbeskrivelse,

  constraint "Same bygningsdelsbeskrivelse cannot be referenced more than once for the same arbejdsbeskrivelse_section"
    unique (arbejdsbeskrivelse_section_id, bygningsdelsbeskrivelse_id)
);

-- sqlite treats all null values as different, so a constraint like
-- unique (parent_id, section_no) won't do, because parent_id can be null.
create unique index arbejdsbeskrivelse_section_unique_idx
on arbejdsbeskrivelse_section (
  arbejdsbeskrivelse_id,
  ifnull(parent_id, -1),
  section_no
);

-------------------------------------------------------------------------------
-- Helper views
-------------------------------------------------------------------------------

/**

Description:

  `arbejdsbeskrivelse_section` is a self-referencing table where sections might
  have 0 to many sub sections. This view can be joined for useful columns when
  displaying the tree.

Columns:

  arbejdsbeskrivelse_section_id integer
    Used for joins.

  section_path text
    Contains the section_no path to the row, separated by a dot (.)
    If parent_id points to a parent with section_no = 3 and the row contains
    section_no = 1, then section_path = 3.1

  level integer
    The level in the tree of sections, starting at 0.

Example:

  select * from arbejdsbeskrivelse_section
  natural join arbejdsbeskrivelse_section_path
  order by section_path;

*/
create view arbejdsbeskrivelse_section_path as
  with recursive tree (
    arbejdsbeskrivelse_section_id,
    section_no,
    section_path,
    level
  ) as (
    select
      arbejdsbeskrivelse_section_id,
      section_no,
      cast(section_no as text),
      0 as level
    from arbejdsbeskrivelse_section
    where parent_id is null
    union all
    select
      node.arbejdsbeskrivelse_section_id,
      node.section_no,
      tree.section_path || '.' || node.section_no,
      tree.level + 1
    from arbejdsbeskrivelse_section node, tree
    where node.parent_id = tree.arbejdsbeskrivelse_section_id
  )
  select arbejdsbeskrivelse_section_id, section_path, level from tree;

/**

Description:

  `bygningsdelsbeskrivelse_section` is a self-referencing table where sections might
  have 0 to many sub sections. This view can be joined for useful columns when
  displaying the tree.

Columns:

  bygningsdelsbeskrivelse_section_id integer
    Used for joins.

  section_path text
    Contains the section_no path to the row, separated by a dot (.)
    If parent_id points to a parent with section_no = 3 and the row contains
    section_no = 1, then section_path = 3.1

  level integer
    The level in the tree of sections, starting at 0.

Example:

  select * from bygningsdelsbeskrivelse_section
  natural join bygningsdelsbeskrivelse_section_path
  order by section_path;

*/
create view bygningsdelsbeskrivelse_section_path as
  with recursive tree (
    bygningsdelsbeskrivelse_section_id,
    section_no,
    section_path,
    level
  ) as (
    select
      bygningsdelsbeskrivelse_section_id,
      section_no,
      cast(section_no as text),
      0 as level
    from bygningsdelsbeskrivelse_section
    where parent_id is null
    union all
    select
      node.bygningsdelsbeskrivelse_section_id,
      node.section_no,
      tree.section_path || '.' || node.section_no,
      tree.level + 1
    from bygningsdelsbeskrivelse_section node, tree
    where node.parent_id = tree.bygningsdelsbeskrivelse_section_id
  )
  select bygningsdelsbeskrivelse_section_id, section_path, level from tree;
