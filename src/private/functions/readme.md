# Functions

This is the folder where your individual private functions should be placed. Depending on the complexity
of your module, you may wish to separate content into subfolders. 

The module will pick up all .ps1 files recursively at load time, however these functions will only be 
visible to your other functions within the module, and they cannot be called outside of it.

In the event that a function should be available to be called directly, you should move it to the 'public'
folder under 'src' instead.