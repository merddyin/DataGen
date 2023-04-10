# Adding new generator packages

Generators are comprised of a 'package' of elements, which consist of several items:

- A ps1 file that matches the name of the generator
- A 2-3 letter prefix/abbreviation to be used for all GenSetOptions
- One or more data files that will reside in the Data folder
- An entry in the CSV in the root of this folder with all associated values filled in
  - Values include
    - Name
    - Prefix/abbreviation (2-3 characters; must be unique)
    - Boolean indicating use of data files
    - Boolean indicating if online data sources are used
    - List of direct generator dependencies (semicolon separated)
    - Generator level (parent, mid, base)

As outlined in the about_DataGen file, generators at each level must comply with very specific characteristics as follows:

- Parent
  - A parent generator is one that calls other generators, and acts as an orchestrator for creating a larger set of inter-connected items
  - Generators of this type can adjust or format items returned to them from other generators, but should typically not be used to perform any data generation themselves
  - A parent generator may never be called by another generator, even by another Parent
  - A parent generator may not have data sources
  - Example: Organization
    - Calls company generator to establish key baseline items
    - Calls a domain generator to create a company top-level domain
    - Calls address generator to create associated locations, all related to the same company
    - Calls hierarchy generator to create positions associated to each location, and ensures that key leadership positions are not duplicated (e.g. multiple CIOs)
    - Calls identity generator for each of the positions to associate a person profile, with each user having the same company domain email address
- Base
  - A base generator represents the smallest level of object generation, and is typically self-contained (no external data or generator needs), which is only called by other generators
  - Generators of this type are not visible to the main Start-DGDataGen cmdlet, only to other generators
  - Generators of this type should still have in-line help, but this is not enforced
  - Generators of this type may never call another generator, and they are typically only called by Mid generators
  - Typically generates only a single data element, which is returned as a string
  - Example: Character
    - Generates and returns a random character (letter, number, or symbol of some time)
    - All generation, select, and return is accomplished within the code, without any calls to external data files or other generators
    - Generator is called by other generators needing a random character, such as the passgen generator for creation of random secure passwords
- Mid   
  - A mid generator is the workhorse of the module, in that it may call one or more generators to create some of its data elements, in addition to performing generation steps itself, either with, or without supplemental data sources
  - Generators of this type may depend on each other, or on base generators, but cannot call parent generators
  - Generators of this type are visible to the Start-DGDataGen cmdlet to be called directly, in addition to being called by Parents
  - Example: Address
    - Calls adjective, noun, and verb base generators as part of street name generation
    - Calls character generator as part of house number and possibly suite generation
    - Uses data import for postalCode and city data elements, with additional code to enable filtering of the address by continent, country, or state/county