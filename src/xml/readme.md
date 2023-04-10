# XML

This is the folder where project XML files go, if any, and specifically:

 - Format XML
 - Type Extension XML

External help files should _not_ be placed in this folder! The help files are generated and placed automatically as part of the build process using PlatyPS.

## Notes on Files and Naming

There should be only one format file and one type extension file per project, as importing more than one of each will have a notable impact on import times.

 - The Format XML should be named `DataGen.Format.ps1xml`
 - The Type Extension XML should be named `DataGen.Types.ps1xml`

Note: Example files are included in this directory for your reference, however these files should be removed if they are not required.

## Helper Functions from the PSFramework module

Note: While this module is included as a plugin for this project, all cmdlets from plugins are only privately available. To leverage these utilities, you will need to install the PSFramework module directly, either by copying the module folder from plugins to your Modules folder, 
or by running the appropriate Install cmdlet for your version of PowerShell (Install-Module pre v6+ or Install-PSResource post)

### New-PSMDFormatTableDefinition

This function will take an example input object and generate format xml for an auto-sized table, and provides a simple way to get started with formats. Once the initial value is set, you can tweak it to meet your specific tastes. This can be helpful for overriding the default formatting
applied to objects by PowerShell. This can be helpful for showing specific properties, or performing calculations (such as file size calculations) as virtual properties without needing to use Select-Object or Add-Member to modify the underlying object. 

### Get-PSFTypeSerializationData

```
C# Warning!
This section is primarily only of interest if you're using C# together with PowerShell. 
```

This function generates the required type extension XML that allows PowerShell to convert types written in C# to be written to a file and then restore from it without the data being 'Deserialized'. 
Note: This also works for jobs or remoting, provided both sides have the `PSFramework` module and the associated type extension loaded. That said, it is possible to remotely load the module and types without them being present on the remote system using a reverse remoting technique (check Google).

In order for a class to be eligible for this, it needs to conform to the following rules:

 - Have the `[Serializable]` attribute
 - Be a public class
 - Have an empty constructor (can have non-empty as well, but must have an empty one as well)
 - Allow all public properties/fields to be set (even if setting it doesn't do anything) without throwing an exception

Note: If using classes defined directly within PowerShell, and not via a compiled assembly, then only the `[Serializable]` attribute and the empty constructor matters. This is because all classes defined directly in the shell are public, and all properties are technically settable. There is a way to
'fake' a read-only attribute, however it is not possible to create one. This is due to the fact that PowerShell acts as both the runtime environment and the debugger, and so it must be able to see all classes and properties defined at runtime. 

```
non-public properties from an assembly, or hidden properties and fake 'non-settable' properties from runtime defined classes, will not be accounted for by the cmdlet!
```