# scriptblocks

This is the folder where you can store short script blocks that can be used to populate parameter validators.

As indicated in the scriptblocks.ps1, you can create one or more key-value pairs, where the key is a reference name, and the value is a block of code that results in verification of the parameter value being provided.

As an example, say you want to verify that a file exists in a certain path. Instead of using ValidateScript, you would use the following:

ScriptBlock
Set-PSFScriptblock -Name 'PSFP.VerifyFile' -Scriptblock {
	Test-Path $_
}

To use it, instead of using '[ValidateScript({Test-Path $_})]' on the parameter, you would use '[PSFValidateScript('PSFP.VerifyFile', ErrorMessage = 'Error procesing {0} - the path must exist')]'