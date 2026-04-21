$message = $PluginRequest.PluginSettings['StatusMessage']
if ($null -eq $message -or $message -eq '') {
  $message = 'first-party-noop-ready'
}

New-PluginResult -Records @() -Warnings @($message)
