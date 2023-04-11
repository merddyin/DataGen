<#
# Example - PSFramework:
    Register-PSFTeppArgumentCompleter -Command Get-Alcohol -Parameter Type -Name ENVR.alcohol

# Example - Native:
    Register-ArgumentCompleter -CommandName Get-Alchohol -Parameter Type -Name ENVR.alcohol
#>
Register-ArgumentCompleter -CommandName Start-DGNDataGen -Parameter DataSet -Name DGNdataset