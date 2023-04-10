# Tab Expansion

## Description

Tab Expansion was originally opened to users with the module `Tab Expansion Plus Plus` (TEPP). Modern tab expansion functionality was later added natively in version 5.

In short, tab completion enables you to define what options a user is offered when tabbing through input values for a particular parameter, which is a key usability element for PowerShell.

The `PSFramework` offers a simplified way of offering just this for versions of PowerShell prior to 5. Alternatively, in PowerShell v5 or newer, you can leverage the native option to achieve
a similar experience. If you wish for your module to support all versions of PowerShell (v3 or newer), then it is recommended that the PSFramework option is leveraged to enable consistency.

## Concept - PSFramework

Custom tab completion is defined in two steps:

 - Define a scriptblock that is run when the user hits `TAB` and provides the strings that are his options. (in src\private\tepp named as per below)
 - Assign that scriptblock to the parameter of a command using the Register-PSFTeppScriptblock cmdlet. Note: You can assign the same scriptblock multiple times.

## Guidance

Import order matters. In order to make things work with the default scaffold, follow these rules:

 - Use individual scriptfiles to _define_ scriptblocks that provide the values to be used
 - Name all such files in a meaningful manner, but ensure the nane ends like this: `*.tepp.ps1`
 - Use `assignment.ps1` to define which cmdlets and parameters the argument completer should be used for

See the example files for indications of how to use both the PSFramework and native options