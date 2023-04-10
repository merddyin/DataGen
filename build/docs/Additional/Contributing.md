# Contributing to DataGen

Project Site: [https://github.com/merddyin/DataGen.git](https://github.com/merddyin/DataGen.git)

There are some important things to be aware of if you plan on contributing to this project.

## Documentation

All base project documentation changes should be made against the .\build\docs\Additional markdown files. These will populate and overwrite existing document files within the .\docs folder at build time. Additionally, if ReadTheDocs integration is enabled you should update the .\build\docs\ReadTheDocs markdown files. Note that each folder becomes its own section within ReadTheDocs and its own folder within the .\docs directory.

Finally, the Function documentation gets generated automatically based on the comment based help on each public/exported function. The function documentation markdown automatically gets populated within the .\docs\Functions folder as well as with the module release under its own docs folder. Private function CBH is not required but is encouraged.

## Development Environment

While any text editor will work well enough, there are included task and setting json files explicitly for Visual Studio Code included with this project. The below tasks have been defined to make things a bit easier, and they can be accessed via the 'Pallette' (Shift+Ctrl+P or Shift+Cmd+P), then start typing in any of the following tasks to find and run them:

- Clean -> Cleans out your scratch folder
- Build -> Runs the Build task (also can use Shift+Ctrl+B or Shift+Cmd+B)
- Analyze -> Runs PSScriptAnalyzer against the src/public files.
- CreateProjectHelp - Creates the project level help.
- Test - Runs Pester tests.
- InsertMissingCBH - Analyzes the existing public functions and inserts a template CBH if no CBH already exists and saves it into your scratch folder.

You will also need a copy of Git installed to interact with the repository. While it is not strictly required, I would also recommend Git and GitHub extensions for your VSCode instance. In all, the following extensions to VSCode are recommended, in no particular order:

- Better Comments: Used to track TODO items, questions, and special notes more readily in-line with the code
- GitHub Actions: Useful for interacting with Actions, and seeing results from pushes
- GitHub Pull Request: Allows you to create and interact with Pull Requests without having to open a browser
- GitLens: Helps see who made what changes at a line level
- Pester Tests: This helps you track whether your Pester Tests are passing or failing without needing to run Pester manually
- PowerShell: The official PowerShell extension for syntax highlighting and intellisense
- Todo Tree: Pairs with Better Comments to enable you to see all the tagged notes and ToDo items in one place, and easily jump to the correct location

Finally, all components have to have associated tests that at minimum verify the output from a particular extension is returned properly. This makes it easier to see if something breaks, particularly if there are dependencies on other extensions within the module. This means that you will need at least the Pester module installed. All tests in the current module are built using Pester 5.4 syntax. 

## Work Branches

Even if you have push rights on the repo, you should create a personal fork, and then create feature branches. This keeps the main repo clean, and your personal workflows unimpeded.

## Pull Requests

To enable me to quickly review and accept your PR, please first ensure that there is a logged issue, and create a single PR specific to that issue. Please ensure that you also link the issue within the body of the PR. Do not merge multiple issues into a single PR unless they are all caused by the same root problem. If developing a new extension, please ensure you are following the same coding conventions as shown in the other module extensions, with the below key items kept in mind.

- All extensions must include comment-based help that is fully populated (all parameters covered, real examples covering each use case)
- Follow the same order of operations
- Provide comments outlining what is being done in each code block
- Be clear on the type of output being returned (objects are preferred, but strings or other types are acceptable as long as they are called out)
- Do not use abbreviations or aliases for other cmdlets, functions, or parameters...you need to pass all PSScriptAnalyzer tests

## Discussion Etiquette

In order to keep the conversations clear and transparent, please limit discussion to English, and keep things on topic with the issue. Be considerate to others, and try to maintain a professional demeanor. 