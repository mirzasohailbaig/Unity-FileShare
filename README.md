# UnityFileShare
Sending files through network over TCP in background thread

## Usage
You need to create GameObject in scene and add FileShare.cs script as component. Then you can call it from other scripts.

```c#
FileShare.RegisterReceiveAction("someAction", (file) => { Debug.Log("received file: " + file); });
FileShare.Send("someFile.xml", "someAction");
```


## Explanation
At the start you need action (ex. lambda function) after receive file on client side. Identifier of this action is string. Also you have a option to remove (UnregisterReceiveAction) this action. Progress of sending a file is starting with Unity message, to be prepare to receive file on specified port. After receiving a file, it's invoked action by specified identifier.

## Warning
Unity message are identified by "short" value. This script is using Unity highest (47) + 1. If you are using this message number, you can change it directly in script region "network message stuff".

While you are testing sharing files, use two computers. Windows not allowing open same port for multiple instances.

Tested on Unity 5.5 and 2017.2. Tested only on Windows.

\
If you like this project and you want to support me, buy me a tea :)

[![Donate paypal](https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif)](https://www.paypal.me/MichalStefanak)
