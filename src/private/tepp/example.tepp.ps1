<#
# Example - PSFramework:
    Register-PSFTeppScriptblock -Name "ENVR.alcohol" -ScriptBlock { 'Beer','Mead','Whiskey','Wine','Vodka','Rum (3y)', 'Rum (5y)', 'Rum (7y)' }

    Note: The name includes an abbreviation for the module in the example above. This naming approach ensures that your argument completers don't conflict with any system values, and also follows c# class naming conventions

# Example - Native:
    $ENVR.alcohol = {
        param($commandName,$parameterName,$stringMatch){
            @('Beer','Mead','Whiskey','Wine','Vodka','Rum (3y)', 'Rum (5y)', 'Rum (7y)') | Where-Object{$_ -like "$stringMatch*"} | Sort-Object
        }
    }

    Note: The net effect of either is largely the same, though the native approach shown above will allow a user to type a partial name and prioritize tab completion to that value
    e.g. User can type 'R' and hit the 'Tab' key, and only the Rum options would be cycled through in alphabetical order
#>