# UnityFileShare
Sending files through network over TCP in background thread

## Usage
You need to create GameObject in scene and add FileShare.cs script as component. Then you can call it from other scripts.

```c#
FileShare.RegisterReceiveAction("someAction", (file) => { Debug.Log("received file: " + file); });
FileShare.Send("someFile.xml", "someAction");
```

You can set target directory for received files. For example:
```c#
FileShare.SetTempDirectory(Application.dataPath);
```

## Explanation
At the start you need action (ex. lambda function) after receive file on client side. Identifier of this action is string. Also you have a option to remove (UnregisterReceiveAction) this action. Progress of sending a file is starting with Unity message sended to all clients, to be prepared to receive file on specified port. Client (sender of file) ask server for client addresses and send the file to all of them. Each client after receiving a file invokes action by specified identifier.

## Warning
Unity message are identified by "short" value. This script is using Unity highest (47) +1 and +2. If you are using those message numbers, you can change it directly in script region "network message stuff".

While you are testing sharing files, use two computers. Windows not allowing open same port for multiple instances.

Tested on Unity 5.5 and 2017.2. Tested only on Windows.

\
If you like this project and you want to support me, buy me a tea :)

[![Donate paypal](https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif)](https://www.paypal.me/MichalStefanak)
