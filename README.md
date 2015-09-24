# Windows 10 Background Bluetooth Sample
This sample requires two devices. And allow basic communication in background between the two bluetooth enabled windows 10 devices.

# Instructions
1.	Pair two Windows 10 devices.
2.	Deploy this application on both of these devices.
3.	On Device A, Click on Start Background Server. You may close the app after that.
4.	On Device B, Click on Show Devices. You will see the name of Device A displayed in the app.
5.	On Device B, Click on Send Message to send a message.
6.	On Device A and B both you will receive a prompt to confirm if you want to give this app permission for Bluetooth communication.
7.	Go back to Device A, click on “Fetch Message” to get latest DateTime pushed by Device B.
8.	Once permission is granted, you can start background worker task by clicking on “Register BG Worker”.
9.	Every half an hour Device B will push current DateTime to Device A.
10.	While debugging, you can explicitly call background task to get the desired behaviour.
