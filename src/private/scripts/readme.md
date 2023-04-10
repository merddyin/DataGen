# Scripts

This folder contains miscellaneous scripts that should be run as part of your module initialization

The module will pick up all .ps1 files recursively

## license

This loads a current copy of the license into memory as a variable

## strings
This is where the strings go, that are written by
# Write-PSFMessage, Stop-PSFFunction or the PSFramework validation scriptblocks
This file loads the strings documents from the respective language folders, and is where the strings 
go that are written out by Write-PSFMessage, Stop-PSFFunction, or for the PSFramework validation 
scriptblocks descripted in the scriptblocks folder.
This allows localizing messages and errors for each supported language, with a psd1 file for each.
Partial translations are acceptable - when missing a current language message, PowerShell automatically
falls back to the language of the local system, or English if all else fails.