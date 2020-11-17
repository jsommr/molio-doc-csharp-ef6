-- Enfore foreign key constraints
pragma foreign_keys = on;

-- Document version. Incrementet when the schema changes.
pragma user_version = 1;

-------------------------------------------------------------------------------
-- Project
-------------------------------------------------------------------------------

create table project (
  project_id      integer primary key,
  project_guid    blob    not null,
  project_version integer not null,
  
  constraint "project_guid is not a valid guid"
  check (
    typeof(project_guid) = 'blob' and
    length(project_guid) = 16
  ),

  constraint "Non-integer value used for project_version"
  check (typeof(project_version) = 'integer')
);

-------------------------------------------------------------------------------
-- Attachment
-------------------------------------------------------------------------------

create table attachment (
  attachment_id integer primary key,
  name          text    not null,
  mime_type     text    not null,
  content       blob    not null, -- Encode as UTF-8 if storing text
  hash          blob,             -- SHA1 hash (optional)

  constraint "content is not a blob" check (typeof(content) = 'blob'),

  constraint "hash is not a valid SHA1 hash"
  check (
    hash is null or
    ( typeof(hash) = 'blob' and
      length(hash) = 20 )),

  constraint "duplicate hash detected" unique (hash)
);

-------------------------------------------------------------------------------
-- Construction Element Specification
-------------------------------------------------------------------------------

create table construction_element_specification (
  construction_element_specification_id integer primary key,
  molio_specification_guid              blob    not null,
  title                                 text    not null,

  constraint "molio_specification_guid is not a valid guid"
  check (
    typeof(molio_specification_guid) = 'blob' and
    length(molio_specification_guid) = 16
  )
);

create table construction_element_specification_section (
  construction_element_specification_section_id integer primary key,
  construction_element_specification_id         integer not null,
  section_no                                    integer not null,
  heading                                       text    not null,
  body                                          text    not null default '',
  molio_section_guid                            blob,
  parent_id                                     integer,

  foreign key (construction_element_specification_id)
  references construction_element_specification,

  foreign key (parent_id)
  references construction_element_specification_section,

  constraint "molio_section_guid is not a valid guid"
  check (
    molio_section_guid is null or
    ( typeof(molio_section_guid) = 'blob' and
      length(molio_section_guid) = 16 )),

  constraint "Non-integer value used for section_no"
  check (typeof(section_no) = 'integer')
);

create unique index construction_element_specification_section_unique_idx
on construction_element_specification_section (
  construction_element_specification_id,
  ifnull(parent_id, -1), -- All nulls are treated as unique, convert to -1 instead
  section_no
);

-------------------------------------------------------------------------------
-- Work Specification
-------------------------------------------------------------------------------

create table work_specification (
  work_specification_id integer primary key,
  work_area_code        text    not null,
  work_area_name        text    not null,

  -- Key is used to associate external data with an work_specification
  -- and must be the same for every export of this project
  key                   blob    not null,

  constraint "key is not a valid guid"
  check (
    typeof(key) = 'blob' and
    length(key) = 16
  )
);

create table work_specification_section (
  work_specification_section_id integer primary key,
  work_specification_id         integer not null,
  section_no                    int     not null,
  heading                       text    not null,
  body                          text    not null default '',
  molio_section_guid            blob,
  parent_id                     integer,

  foreign key (work_specification_id) references work_specification,
  foreign key (parent_id) references work_specification_section,

  constraint "molio_section_guid is not a valid guid"
  check (
    molio_section_guid is null or
    ( typeof(molio_section_guid) = 'blob' and
      length(molio_section_guid) = 16 )),

  constraint "Non-integer value used for section_no"
  check (typeof(section_no) = 'integer')
);

create table work_specification_section_construction_element_specification (
  work_specification_section_construction_element_specification_id integer primary key,
  work_specification_section_id                                    integer not null,
  construction_element_specification_id                            integer not null,

  foreign key (work_specification_section_id)
  references work_specification_section,

  foreign key (construction_element_specification_id)
  references construction_element_specification,

  constraint "Same construction_element_specification cannot be referenced more than once for the same work_specification_section"
  unique (work_specification_section_id, construction_element_specification_id)
);

create unique index work_specification_section_unique_idx
on work_specification_section (
  work_specification_id,
  ifnull(parent_id, -1), -- All nulls are treated as unique, convert to -1 instead
  section_no
);

-------------------------------------------------------------------------------
-- Custom Data
-------------------------------------------------------------------------------

-- Used to store any kind of custom key-value pairs
create table custom_data (
  key   text primary key,
  value blob
);

-------------------------------------------------------------------------------
-- Helper views
-------------------------------------------------------------------------------

/**

Description:

  `work_specification_section` is a self-referencing table where sections might
  have 0 to many sub sections. This view can be joined for useful columns when
  displaying the tree.

Columns:

  work_specification_section_id integer
    Used for joins.

  section_path text
    Contains the section_no path to the row, separated by a dot (.)
    If parent_id points to a parent with section_no = 3 and the row contains
    section_no = 1, then section_path = 3.1

  level integer
    The level in the tree of sections, starting at 0.

Example:

  select * from work_specification_section
  natural join work_specification_section_path
  order by section_path;

*/
create view work_specification_section_path as
  with recursive tree (
    work_specification_section_id,
    section_no,
    section_path,
    level
  ) as (
    select
      work_specification_section_id,
      section_no,
      cast(section_no as text),
      0 as level
    from work_specification_section
    where parent_id is null
    union all
    select
      node.work_specification_section_id,
      node.section_no,
      tree.section_path || '.' || node.section_no,
      tree.level + 1
    from work_specification_section node, tree
    where node.parent_id = tree.work_specification_section_id
  )
  select work_specification_section_id, section_path, level from tree;

/**

Description:

  `construction_element_specification_section` is a self-referencing table where sections might
  have 0 to many sub sections. This view can be joined for useful columns when
  displaying the tree.

Columns:

  construction_element_specification_section_id integer
    Used for joins.

  section_path text
    Contains the section_no path to the row, separated by a dot (.)
    If parent_id points to a parent with section_no = 3 and the row contains
    section_no = 1, then section_path = 3.1

  level integer
    The level in the tree of sections, starting at 0.

Example:

  select * from construction_element_specification_section
  natural join construction_element_specification_section_path
  order by section_path;

*/
create view construction_element_specification_section_path as
  with recursive tree (
    construction_element_specification_section_id,
    section_no,
    section_path,
    level
  ) as (
    select
      construction_element_specification_section_id,
      section_no,
      cast(section_no as text),
      0 as level
    from construction_element_specification_section
    where parent_id is null
    union all
    select
      node.construction_element_specification_section_id,
      node.section_no,
      tree.section_path || '.' || node.section_no,
      tree.level + 1
    from construction_element_specification_section node, tree
    where node.parent_id = tree.construction_element_specification_section_id
  )
  select construction_element_specification_section_id, section_path, level from tree;
