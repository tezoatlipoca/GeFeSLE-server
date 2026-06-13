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


async function Login(loginto) {
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

        let form = document.getElementById("loginForm");
        if (loginto === undefined || loginto === "local") {
            let formData = new FormData(form);

            try {
                let response = await fetch('/me', {
                    method: 'POST',
                    headers: {
                        "GeFeSLE-XMLHttpRequest": "true"
                    },
                    credentials: 'include',
                    body: formData
                });

                response = await handleResponse(response);
                let json = await response.json();

                if (json && json.antiForgeryToken) {
                    localStorage.setItem('antiForgeryToken', json.antiForgeryToken);
                }
                if (json && json.antiForgeryHeaderName) {
                    localStorage.setItem('antiForgeryHeaderName', json.antiForgeryHeaderName);
                }

                window.location.href = '/';
            }
            catch (error) {
                console.error(fn, error);
                d(error.message || error);
                c(RC.ERROR);
            }
        }
        else {
            // OAuth remains form-submit/redirect based
            form.submit();
        }
    }
}