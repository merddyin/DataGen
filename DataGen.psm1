# Current script path
[string]$ModulePath = Split-Path (get-variable myinvocation -scope script).value.Mycommand.Definition -Parent
$Script:ModuleRoot = $PSScriptRoot
$Script:ModuleVersion = (Import-PowerShellDataFile -Path "$($script:ModuleRoot)\TestMePSFProject.psd1").ModuleVersion

function Import-ModuleFile
{
	<#
		.SYNOPSIS
			Loads files into the module on module import.
		
		.DESCRIPTION
			This helper function is used during module initialization.
			It should always be dotsourced itself, in order to proper function.
			
			This provides a central location to react to files being imported, if later desired
		
		.PARAMETER Path
			The path to the file to load
		
		.EXAMPLE
			PS C:\> . Import-ModuleFile -File $function.FullName
	
			Imports the file stored in $function according to import policy
	#>
	[CmdletBinding()]
	Param (
		[string]
		$Path
	)
	
	$resolvedPath = $ExecutionContext.SessionState.Path.GetResolvedPSPathFromPSPath($Path).ProviderPath
	if ($doDotSource) { . $resolvedPath }
	else { $ExecutionContext.InvokeCommand.InvokeScript($false, ([scriptblock]::Create([io.file]::ReadAllText($resolvedPath))), $null, $null) }
}

# Module Pre-Load code
foreach ($path in (& "$ModuleRoot\src\other\PreLoad.ps1")){
	. Import-ModuleFile -Path $path
}

# Private and other methods and variables
foreach($function in (Get-ChildItem "$ModuleRoot\src\private\functions" -Filter "*.ps1" -Recurse -ErrorAction Ignore)){
	Write-Verbose "Dot sourcing private script file: $($_.Name)"
	. Import-ModuleFile -Path $function.FullName
}

# Load and export public methods
foreach($function in (Get-ChildItem "$ModuleRoot\src\public" -Recurse -Filter "*.ps1" -ErrorAction Ignore)){
	Write-Verbose "Dot sourcing public script file: $($FunctionFile.Name)"
	. Import-ModuleFile -Path $function.FullName

	# Find all the functions defined no deeper than the first level deep and export it.
	$Content = Get-Content -Path $($function.FullName)
	if ($Content) {
		$ASTdata = [System.Management.Automation.Language.Parser]::ParseInput(($Content), [ref]$null, [ref]$null)
		Export-ModuleMember ($ASTdata.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)).Name
	}
	else {
		Write-Verbose "FileName: $($FunctionFile.Name)`tIssue: No data, skipping export"
	}
}

# Load any configuration data
foreach($function in (Get-ChildItem "$ModuleRoot\src\private\configurations" -Recurse -Filter "*.ps1" -ErrorAction Ignore)){
	Write-Verbose "Dot sourcing configuration file: $($_.Name)"
	. Import-ModuleFile -Path $function.FullName
}

# Load any script blocks
foreach($function in (Get-ChildItem "$ModuleRoot\private\scriptblocks" -Recurse -Filter "*.ps1" -ErrorAction Ignore)){
	Write-Verbose "Dot sourcing scriptblock file: $($_.Name)"
	. Import-ModuleFile -Path $function.FullName
}

# Load tab expansion details
foreach($function in (Get-ChildItem "$ModuleRoot\private\tepp" -Recurse -Filter "*.tepp.ps1" -ErrorAction Ignore)){
	Write-Verbose "Dot sourcing tab expansion file: $($_.Name)"
	. Import-ModuleFile -Path $function.FullName
}

# Load tab expansion assignments - must occur after definitions
foreach ($path in (& "$ModuleRoot\private\tepp\assignment.ps1")){
	. Import-ModuleFile -Path $path
}

# Load license
foreach ($path in (& "$ModuleRoot\private\scripts\license.ps1")){
	. Import-ModuleFile -Path $path
}

# Load generators
foreach ($function in (Get-ChildItem "$ModuleRoot\Generators" -Recurse -Filter "*.ps1" -ErrorAction Ignore)){
	Write-Verbose "Dot sourcing generator file: $($_.Name)"
	. Import-ModuleFile -Path $function.FullName
}

# Module Post-Load code
. (Join-Path $ModulePath 'src\other\PostLoad.ps1')