function setRoles() {
    let fn = "setRoles"; console.debug(fn);
    if (islocal()) return;


    // get the username from the form
    let userid = document.getElementById('userid').value;
    let username = document.getElementById('username').value;
    // get the selected role from the roles dropdown
    let role = document.getElementById('role').value;
    console.debug(fn + ': ' + username + ' - ' + role);

    if (username == null || username == '' || role == null || role == '') {
        d('Cant set role for user: user/role cannot be blank!');
        c(RC.ERROR);
    }

    let apiUrl = '/users/' + userid + '/roles';

    let postdata = [role];
    console.debug(fn + ' calling: ' + apiUrl + ' with data: ' + JSON.stringify(postdata));
    fetch(apiUrl,
        {
            method: 'POST',
            headers: {
                "GeFeSLE-XMLHttpRequest": "true",
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(postdata)
        })
        .then(handleResponse)
        .then(response => {
            d('Role ' + role + 'for ' + username + ' SET!');
            c(RC.OK);
            // wait 1 seconds then call getRoles to refresh the roles list
            setTimeout(getRoles, 1000);
            return;

        })
        .catch((error) => {
            console.error('Error:', error);
            d(error);
            c(RC.ERROR);
        });
}




function getRoles() {
    let fn = 'getRoles'; console.debug(fn);

    if (islocal()) return;

    // get the username from the form
    let userid = document.getElementById('userid').value;
    console.debug(`${fn}:${userid}`);
    if (userid == null || userid == '') {
        return;
    }

    // Get the list data from the API
    let apiUrl = '/users/' + userid + '/roles';
    console.debug(fn + ' --> ' + apiUrl);
    fetch(apiUrl, {
        headers: {
            "GeFeSLE-XMLHttpRequest": "true"
        }
    }).then(handleResponse)
        .then(response => {

            if (response.status === RC.NO_CONTENT) {
                let userroles = document.getElementById('userroles');
                userroles.innerHTML = '&lt;no roles&gt;';
                return null;
            }
            else {
                return response.json();
            }
        })
        .then(json => {
            if (json == null) return;
            else {
                userroles.innerHTML = json.join(', ');
            }
        })
        .catch((error) => {
            console.error(error);
            d(error);
            c(RC.ERROR);
        });

}

function getUser() {
    let fn = "getUser"; console.debug(fn);

    if (islocal()) return;

    let urlParams = new URLSearchParams(window.location.search);
    let username = urlParams.get('username');
    let userid = urlParams.get('userid');
    console.debug(`${fn} - username: ${username} - userid: ${userid}`);
    if ((username == null || username == '') && (userid == null || userid == '')) {
        return;
    }
    let apiUrl = '/users/' + username;
    console.debug(`${fn} --> ${apiUrl}`);
    // Get the list data from the API

    fetch(apiUrl, {
        headers: {
            "GeFeSLE-XMLHttpRequest": "true"
        }
    })
        .then(response => response.json())
        .then(json => {
            // populate the form with the user data
            document.getElementById('userid').value = json.id;
            document.getElementById('username').value = json.userName;
            document.getElementById('email').value = json.email;
            d("User " + json.userName + " retreived!");
            c(RC.OK);
            getRoles();
        })
        .catch((error) => {
            console.error(error);
            d(error);
            c(RC.ERROR);
        });

}

async function updateoraddUser(e) {
    e.preventDefault();
    if (islocal()) return;
    console.log('updateoraddUser');

    if (document.getElementById('userid').value == '') {
        addUser(e);
    }
    else {
        modifyUser(e);

    }


}

// function for when changepassword button is pressed. Gets value of username 
// and password from
// the form and ends it to API /setpassword
async function changePassword(e) {
    e.preventDefault();
    if (islocal()) return;
    console.log('CHANGEPASSWORD');
    let apiUrl = "";

    // username or password cannot be blank
    let userName = document.getElementById('username').value;
    let newPassword = document.getElementById('password').value;
    if (userName == '' || newPassword == '') {
        d('Username or password cannot be blank!');
        c(RC.ERROR);
        return;
    }


    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edituser.html'));
    apiUrl = apiUrl + '/setpassword';

    // construct the user object
    let user = {
        userName: userName,
        newPassword: newPassword
    };

    console.info('CHANGEPASSWORD calling: ' + apiUrl + ' with data: ' + JSON.stringify(user));
    fetch(apiUrl, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
        body: JSON.stringify(user)
    })
        .then(handleResponse)
        .then(response => {
            d('Password for ' + userName + ' changed!');
            c(RC.OK);
            return;
        })
        .catch(error => {
            d(error);
            c(RC.ERROR);
            console.error('Error:', error);
        });
}



async function addUser(e) {
    let fn = 'addUser'; console.debug(fn);

    e.preventDefault();
    if (islocal()) return;

    let apiUrl = '/users';
    // construct the IdentityUser object
    let user = {
        userName: document.getElementById('username').value,
        email: document.getElementById('email').value,
        password: document.getElementById('newPassword').value
    };

    console.debug(fn + ' calling API: ' + apiUrl + ' with data: ' + JSON.stringify(user));
    fetch(apiUrl, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
        body: JSON.stringify(user)
    })
        .then(handleResponse)
        .then(response => {

            console.debug('ADDUSER - new user ADD ok!')
            d('User added:' + JSON.stringify(user));
            c(RC.CREATED);
            response.json().then(data => {


                // now that we have newID, populdate the id field in the form
                document.getElementById('userid').value = data.id;
                document.getElementById('username').value = data.userName;
                document.getElementById('newPassword').value = '';
                document.getElementById('email').value = data.email;

                // and update the url in the browser to include the new username
                // RECONSIDER THIS
                let newUrl = window.location.href;
                newUrl = newUrl.substring(0, newUrl.indexOf('?'));
                newUrl = newUrl + '?username=' + data.userName;
                window.history.pushState({}, '', newUrl);
                console.debug('ADDUSER | New URL: ' + newUrl);
                // wait 1 seconds then call getUsers to refresh the users list
                setTimeout(getUsers, 1000);

            })


        })
        .catch(error => {
            d(error);
            c(RC.ERROR);
            console.error(error);
        });
}

async function clearForm(e) {
    e.preventDefault();
    let fn = 'newUser'; console.debug(fn);
    document.getElementById('userid').value = '';
    document.getElementById('username').value = '';
    document.getElementById('newPassword').value = '';
    document.getElementById('email').value = '';
    // change the label on the submit button to say "Add User"
    document.getElementById('submitbutton').value = 'Add User';
}

async function modifyUser(e) {
    e.preventDefault();
    let fn = "modifyUser"; console.debug(fn);
    if (islocal()) return;
    let userid = document.getElementById('userid').value;
    if (userid == null || userid == '') {
        d('User ID cannot be blank!');
        c(RC.ERROR);
        return;
    }
    let apiUrl = `/users/{$userid}`;
    let user = {
        id: userid,
        userName: document.getElementById('username').value,
        email: document.getElementById('email').value,
    };

    console.info(`${fn} --> ${apiUrl} + ${JSON.stringify(user)}`);
    fetch(apiUrl, {
        method: 'PUT',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
        body: JSON.stringify(user)
    })
        .then(handleResponse)
        .then(response => {
            // if the user is updated ok, we get a 200
            // if the user (by ID) doesn't exist, we'll get a 404
            // if anything else happens in /modifyuser, we'll get a bad request
            d('User ' + user.userName + ' modified!');
            c(RC.OK);
            return;
        })
        .catch((error) => {
            d(error);
            c(RC.ERROR);
            console.error('Error:', error);
        })
}

document.addEventListener('DOMContentLoaded', getUser);
document.addEventListener('DOMContentLoaded', getUsers);

// When the form is submitted, send it to the REST API
document.getElementById('edituserform').addEventListener('submit', updateoraddUser);

// get the addRole button and add an event listener to it
document.getElementById('addRole').addEventListener('click', setRoles);
document.getElementById('deleteRole').addEventListener('click', deleteRole);



function getUsers() {
    let fn = "getUsers"; console.debug(fn);
    if (islocal()) return;

    let apiUrl = '/users';
    console.debug(`${fn} --> ${apiUrl}`);

    // Get the list data from the API
    fetch(apiUrl, {
        headers: {
            "GeFeSLE-XMLHttpRequest": "true"

        }
    })
        .then(handleResponse)
        .then(response => response.json())
        .then(data => {
            let users = document.getElementById('userlistgrid');

            // if there are no results in data
            if (data.length == 0) {
                users.innerHTML = 'No users!';
                d('No users found!');
                c(RC.NOTFOUND);
                return;
            }
            // populate the users list
            let userlist = '<ol>';
            data.forEach((user) => {
                userlist += '<li><a href=\"_edituser.html?username=' + user.userName + '\">' + user.userName + '</a>';
                userlist += ' - email: ' + user.email + ' <-- <a href=\"#\" onclick=\"deleteUser(\'' + user.id + '\')\">DELETE</a></li>';
            });
            userlist += '</ol>';
            users.innerHTML = userlist;
        })
        .catch((error) => {
            console.error(error);
            d(error);
            c(RC.ERROR);
        });



}

function deleteRole() {
    let fn = "deleteRole"; console.debug(fn);
    if (islocal()) return;

    // get the username from the form
    let userid = document.getElementById('userid').value;
    // get the selected role from the roles dropdown
    let role = document.getElementById('role').value;
    console.debug(`${fn} - ${userid} --${role}`);

    if (userid == null || userid == '' || role == null || role == '') {
        d('Cant delete role for user: user id/role cannot be blank!');
        c(RC.ERROR);
        return;
    }

    let apiUrl = `/users/${userid}/roles/${role}`;
    console.debug(`${fn} --> ${apiUrl}`);
    fetch(apiUrl, {
        method: 'DELETE',
        headers: {
            "GeFeSLE-XMLHttpRequest": "true"
        }
    }).then(handleResponse)
        .then(response => {

            d(`Role ${role}  for user ${userid} UNASSIGNED (or it wasn't already)`);
            c(RC.OK);
            // wait 1 seconds then call getRoles to refresh the roles list
            setTimeout(getRoles, 1000);
            return;


        })
        .catch((error) => {
            console.error(error);
            d(error);
            c(RC.ERROR);
        });
}

function deleteUser(userid) {
    event.preventDefault();
    let fn = "deleteUser"; console.debug(fn);

    if (islocal()) return;

    if (userid == null || userid == '') {
        d('User ID cannot be blank!');
        c(RC.ERROR);
        return;
    }
    let apiUrl = `/users/${userid}`;
    console.debug(`${fn} --> ${apiUrl}`);
    fetch(apiUrl, {
        method: 'DELETE',
        headers: {
            "GeFeSLE-XMLHttpRequest": "true"
        }
    })
        .then(handleResponse)
        .then(response => {
            d('User ' + userid + ' deleted!');
            c(RC.OK);
            // clear the form
            document.getElementById('username').value = '';
            document.getElementById('newPassword').value = '';
            document.getElementById('email').value = '';
            document.getElementById('userid').value = '';
            // redirect the URL of the page to just the list (in case we deleted the user we were looking at)
            window.location.href = '/_edituser.html';

            // wait 1 seconds then call getUsers to refresh the users list
            setTimeout(getUsers, 1000);
            return;
        })
        .catch((error) => {
            console.error(error);
            d(error);
            c(RC.ERROR);
        });


}