function LoginDto({OAuthProvider = null,
                Instance = null,
                Username = null,
                Password = null}) {
    this.OAuthProvider = OAuthProvider;
    this.Instance = Instance;
    this.Username = Username;
    this.Password = Password;
    this.isUseless = function() {
        if (!this.OAuthProvider && !this.Instance && !this.Username && !this.Password) {
            return true;
        }
    }
    };


function Login(loginto) {
    let fn = "Login |"; console.debug(fn);
    let loginDto = null;
    if (loginto === undefined || loginto === "local") {
        let Username = document.getElementById("Username").value;
        let Password = document.getElementById("Password").value;
        document.getElementById("OAuthProvider").value = "";
        loginDto = new LoginDto({Username: Username, Password: Password});
    }
    else {
        let Instance = document.getElementById("Instance").value;
        document.getElementById("OAuthProvider").value = loginto;
        console.debug(fn, "loginto: ", loginto);
        loginDto = new LoginDto({OAuthProvider: loginto, Instance: Instance});
        
    }
    if (loginDto.isUseless()) {
        console.debug(fn, "loginDto.isUseless: ", loginDto);
        d("You have to fill in the form with something.");
        c(RC.ERROR);
    }
    else {
        console.debug(fn, "loginDto/sending: ", loginDto);

        // submit the form
        let form = document.getElementById("loginForm");
        form.submit();
    }
}