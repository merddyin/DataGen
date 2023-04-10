function Start-DGDataGen {
    <#
    .SYNOPSIS
        Starts generation of the specified data set    

    .DESCRIPTION
        Starts generation of the specified data set provided to the DataSet parameter, and generates the specified number of values of that item type.
        By default, all generated items will be created as PSObjects to enable integration with the PowerShell pipeline as part of a deployment or 
        provisioning process.

    .PARAMETER DataSet
        Specifies the type of data set to generate. This can be any of three types of DataSet type; Parent, Mid, or Base. A Parent is intended to call
        one or more Mid or Base dataset generators and orchestration creation of a higher level object, depending on the applied DataFilter value. For
        example, the Organization parent generator will, by default, generate an entire company profile, associated location information, employee
        details, associated usernames, etc. If using a FilterSet of 'Base' however, only high-level organization information is generated, so there are
        no data details behind the item.

        If no value is specified, a list of available templates is displayed.

        Note: A Parent template does not generate any data of it's own (see about_DataGen for details)

    .PARAMETER FilterSet
        This parameter determines the depth of data to be returned. This value is passed to the child generator template(s), which tells those templates
        how much data to return. The number of data elements included at each filter level is determined by the template author.

    .Parameter GenSetOptions
        This parameter accepts one or more strings that provide additional execution parameters to the generators at runtime. Accepted values are generator
        specific, but follow a standard convention. For additional details, see the Get-DataSet cmdlet, which provides details on all supported DataSets,
        what values are returned with different FilterSets, and any supplemental GenSet options that may be available.

    .PARAMETER Count
        This parameter specifies the number of items of the specified data type to be generated.

        Note: This parameter is ignored when used with DataSets of type 'Parent', as only one ParentSet can be generated at a time. The number of items
        included within a ParentSet is determined through supplemental values that are passed, or such is determined by other factors dynamically based
        on the template definitions.

    .EXAMPLE
        TBD

    .NOTES
        TBD
    #>
    [CmdletBinding()]
    param (
        [Parameter()]
        [string]$DataSet,

        [Parameter()]
        [string]$FilterSet,

        [Parameter()]
        [string[]]$GenSetOptions

        [Parameter()]
        [int]$Count = 1
    )
    
    begin {
        
    }
    
    process {
        
    }
    
    end {
        
    }
    
}