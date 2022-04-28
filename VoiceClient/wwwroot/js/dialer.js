export function createDialer() {
    return {
        device: null,
        call: null,
        dotNetObjectReference: null,
        setDotNetObjectReference: function (dotNetObjectReference) {
            this.dotNetObjectReference = dotNetObjectReference;
        },
        setupTwilioDevice: function (jwtToken) {
            const device = new Twilio.Device(jwtToken,
                {
                    closeProtection: true // will warn user if you try to close browser window during an active call
                }
            );
            this.device = device;

            device.on('registered', () => {
                this.dotNetObjectReference.invokeMethod('OnTwilioDeviceRegistered');
            });

            device.on('error', error => {
                console.error(error);
                this.dotNetObjectReference.invokeMethod('OnTwilioDeviceError', error.message);
            });

            device.on('incoming', call => {
                this.call = call;
                this.setupCallEvents(call);
                this.dotNetObjectReference.invokeMethod('OnTwilioDeviceIncomingConnection', call.parameters['From']);
            });

            device.register();
        },
        setupCallEvents: function (call) {
            call.on('accept', () => {
                this.dotNetObjectReference.invokeMethod('OnTwilioCallAccepted', call.parameters['From']);
            });
            call.on('cancel', () => {
                this.dotNetObjectReference.invokeMethod('OnTwilioCallCancelled');
                this.call = null;
            });
            call.on('disconnect', () => {
                this.dotNetObjectReference.invokeMethod('OnTwilioCallDisconnected');
                this.call = null;
            });
            call.on('reject', () => {
                this.dotNetObjectReference.invokeMethod('OnTwilioCallRejected');
                this.call = null;
            });
            call.on('error', (error) => {
                console.error(error);
                this.dotNetObjectReference.invokeMethod('OnTwilioCallError', error.message);
            });
        },
        startCall: async function (phoneNumber) {
            // To parameter is a defined property by Twilio, but you could just as well use any other property name
            // and it will be passed to your TwiML webhook as meta data
            this.call = await this.device.connect({params: {"To": phoneNumber}});
            this.setupCallEvents(this.call);
        },
        endCall: function () {
            if (this.call) {
                this.call.disconnect();
            }
        },
        acceptCall: function () {
            if (this.call) {
                this.call.accept();
            }
        },
        rejectCall: function () {
            if (this.call) {
                this.call.reject();
            }
        },
        destroy: function () {
            this.call = null;
            this.device.destroy();
        }
    }
}