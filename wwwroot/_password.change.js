async function getResetToken() {
    let fn = 'getResetToken';
    console.info(fn);
    let [userid, username, role] = await amloggedin();
    try {
        let serverUrl = window.location.origin;
        apiUrl = serverUrl + '/users/' + userid + '/password';
        console.debug(fn + ' | API URL: ' + apiUrl);

        // Perform a GET request
        return await fetch(apiUrl, {
            headers: {
                "GeFeSLE-XMLHttpRequest": "true",
                }})
            .then(response => {
                if (response.ok) {
                    return response.json();
                }
                else if (response.status == RC.NOT_FOUND) {
                    throw new Error('GeFeSLE Server: ' + serverUrl + ' Not Found - check your settings');
                }
                else if (response.status == RC.UNAUTHORIZED) {
                    throw new Error('Not authorized - have you logged in yet? <a href="' + serverUrl + '/login">Login</a>');
                }
                else if (response.status == RC.FORBIDDEN) {
                    throw new Error('Forbidden - have you logged in yet? <a href="' + serverUrl + '/login">Login</a>');
                }
                else {
                    throw new Error('Error ' + response.status + ' - ' + response.statusText);
                }

            })
            .then(text => {
                let resetToken = text;
                resetToken = resetToken.replace(/"/g, '');
                console.debug(fn + ' | API Response: ' + resetToken);
                let idField = document.getElementById('id');
                idField.value = userid;
                let tokenField = document.getElementById('token');
                tokenField.value = resetToken;
                let userField = document.getElementById('userName');
                userField.value = username;
                d('Reset token obtained');
                c(RC.OK);
            });
    } catch (error) {
        console.error('Error:', error);
        d('Error: ' + error);
        c(RC.ERROR);
    }
}

// only if this is the password.change page
if (window.location.pathname.endsWith('_password.change.html')) {
    window.onload = getResetToken;
}

async function sendReset(e) {
    e.preventDefault();
    let fn = 'sendReset';
    console.info(fn);
    let [userid, username, role] = await amloggedin();
    try {
        let serverUrl = window.location.origin;
        apiUrl = serverUrl + '/users/' + userid + '/password';
        console.debug(fn + ' | API URL: ' + apiUrl);

        // create a PasswordChangeDto:
        //     public string? ResetToken { get; set; }
        // public string? NewPassword { get; set; }
        let passwordChangeDto = {
            ResetToken: document.getElementById('token').value,
            NewPassword: document.getElementById('newPassword').value
        }
        let body = JSON.stringify(passwordChangeDto);
        console.debug(fn + ' | API Request: ' + body);
        // Perform a POST request
        return await fetch(apiUrl, {
            method: 'POST',
            headers: {
                "GeFeSLE-XMLHttpRequest": "true",
                "Content-Type": "application/json"
                },
            body: body})
            .then(response => {
                if (response.ok) {
                    return response.text();
                }
                else if (response.status == RC.NOT_FOUND) {
                    throw new Error('GeFeSLE Server: ' + serverUrl + ' Not Found - check your settings');
                }
                else if (response.status == RC.UNAUTHORIZED) {
                    throw new Error('Not authorized - have you logged in yet? <a href="' + serverUrl + '/login">Login</a>');
                }
                else if (response.status == RC.FORBIDDEN) {
                    throw new Error('Forbidden - have you logged in yet? <a href="' + serverUrl + '/login">Login</a>');
                }
                else if(response.status == RC.BAD_REQUEST) {
                    // the response json will be a litany of errors wrong with your pwd:
                    return response.json();
                }
                else {
                    throw new Error('Error ' + response.status + ' - ' + response.statusText);
                }

            })
            .then(json => {
                if (json) {
                    let errors = json;
                    let errorDescriptions = errors.map(error => error.description);
                    let errorMessage = errorDescriptions.join('<br>');
                    d(errorMessage);
                    c(RC.ERROR);
                }
                else {
                    d('Password changed');
                    c(RC.OK);
                }
            });
            
    } catch (error) {
        console.error('Error:', error);
        d('Error: ' + error);
        c(RC.ERROR);
    }
}

async function sendHARDReset(e) {
    e.preventDefault();
    let fn = 'sendReset';
    console.info(fn);
    let [userid, username, role] = await amloggedin();
    try {
        let serverUrl = window.location.origin;
	let resetUser = document.getElementById('userid').value;
        apiUrl = serverUrl + '/users/' + resetUser + '/password';
        console.debug(fn + ' | API URL: ' + apiUrl);

        // create a PasswordChangeDto:
        //     public string? ResetToken { get; set; }
        // public string? NewPassword { get; set; }
        let passwordChangeDto = {
            ResetToken: null,
            NewPassword: document.getElementById('newPassword').value
        }
        let body = JSON.stringify(passwordChangeDto);
        console.debug(fn + ' | API Request: ' + body);
        // Perform a POST request
        return await fetch(apiUrl, {
            method: 'DELETE',
            headers: {
                "GeFeSLE-XMLHttpRequest": "true",
                "Content-Type": "application/json"
                },
            body: body})
            .then(response => {
                if (response.ok) {
                    return response.text();
                }
                else if (response.status == RC.NOT_FOUND) {
                    throw new Error('GeFeSLE Server: ' + serverUrl + ' Not Found - check your settings');
                }
                else if (response.status == RC.UNAUTHORIZED) {
                    throw new Error('Not authorized - have you logged in yet? <a href="' + serverUrl + '/login">Login</a>');
                }
                else if (response.status == RC.FORBIDDEN) {
                    throw new Error('Forbidden - have you logged in yet? <a href="' + serverUrl + '/login">Login</a>');
                }
                else if(response.status == RC.BAD_REQUEST) {
                    // the response json will be a litany of errors wrong with your pwd:
                    return response.json();
                }
                else {
                    throw new Error('Error ' + response.status + ' - ' + response.statusText);
                }

            })
            .then(json => {
                if (json) {
                    let errors = json;
                    let errorDescriptions = errors.map(error => error.description);
                    let errorMessage = errorDescriptions.join('<br>');
                    d(errorMessage);
                    c(RC.ERROR);
                }
                else {
                    d('Password changed');
                    c(RC.OK);
                }
            });
            
    } catch (error) {
        console.error('Error:', error);
        d('Error: ' + error);
        c(RC.ERROR);
    }
}
