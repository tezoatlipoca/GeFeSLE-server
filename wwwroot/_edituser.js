function setRoles() {
    console.log('SETROLES');
    if(islocal()) return;

    // get the username from the form
    let username = document.getElementById('username').value;
    // get the selected role from the roles dropdown
    let role = document.getElementById('role').value;
    console.debug('SETROLES: ' + username + ' - ' + role);

    if(username == null || username == '' || role == null || role == ''){
        d('Cant set role for user: user/role cannot be blank!');
        c(RC.ERROR);
    }

    // assume this page is ON the server with the API
    // get the url of the API from the current page url
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edituser.html'));
    
    // Get the list data from the API
    apiUrl = apiUrl + '/setrole/' + username + '/' + role;
    console.debug('SETROLES - calling: ' + apiUrl);
    fetch(apiUrl, {headers: {
        "GeFeSLE-XMLHttpRequest": "true"
    }})
    .then(response => {
        if (response.status == RC.NOT_FOUND) {
            console.debug('SETROLES - User ' + username + ' not found!');
            throw new Error('User ' + username + ' not found!')
        }
        else if (response.status == RC.OK) {
            d('Role ' + role + 'for ' + username + ' SET!');
            c(RC.OK);
            // wait 3 seconds then call getRoles to refresh the roles list
            setTimeout(getRoles, 3000);
            return;
            
        }
        else if (response.status == RC.UNAUTHORIZED) {
            console.debug('SETROLES - Not authorized to set roles for user ' + username);
            throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
        }
        else if (response.status == RC.FORBIDDEN) {
            console.debug('SETROLES - Forbidden to get user ' + username);
            throw new Error('Forbidden! Are you logged in as an admin?');
        }
        else if (response.status == RC.BAD_REQUEST) {

            response.json().then(data => {

                // its gonna be a collection of code/description pairs
                console.debug('SETROLES - bad something: ' + JSON.stringify(data));
                // iterate over data - its going to be an array
                let msg = '';
                data.forEach((item) => {
                    msg += 'Error: ' + item.code + ' - ' + item.description + '<br>';
                })
                d(msg);
                c(RC.BAD_REQUEST);

            });
        }
        else {
            console.debug('SETROLES - Error Setting user roles' + username);
            throw new Error('Error Setting roles for ' + username);
        }
    })
    .catch((error) => {
        console.error('Error:', error);
        d(error);
        c(RC.ERROR);
    });



}




function getRoles() {
    console.log('GETROLES');
    if(islocal()) return;

    // get the username from the form
    let username = document.getElementById('username').value;
    console.debug('GETROLES: ' + username);
    if(username == null || username == ''){
        return;
    }
    // assume this page is ON the server with the API
    // get the url of the API from the current page url
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edituser.html'));
    
    // Get the list data from the API
    apiUrl = apiUrl + '/getrole/' + username;
    console.debug('GETROLES - calling: ' + apiUrl);
    fetch(apiUrl, {headers: {
        "GeFeSLE-XMLHttpRequest": "true"
    }})
    .then(response => {
        if (response.status == RC.NOT_FOUND) {
            console.debug('GETROLES - User ' + username + ' not found!');
            throw new Error('User ' + username + ' not found!')
        }
        else if (response.status == RC.OK) {
            console.debug('GETROLES - User ' + username + ' roles retreived!');
            return response.text();
        }
        else if (response.status == RC.UNAUTHORIZED) {
            console.debug('GETROLES - Not authorized to get roles for user ' + username);
            throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
        }
        else if (response.status == RC.FORBIDDEN) {
            console.debug('GETROLES - Forbidden to get user ' + username);
            throw new Error('Forbidden! Are you logged in as an admin?');
        }
        else {
            console.debug('GETUSER - Error getting user roles' + username);
            throw new Error('Error getting roles for ' + username);
        }
    })
    .then(data => {
        // the data is going to be a list of the roles
        // highlight the values in the roles dropdown that match the roles in the data
        if(data == null || data == '') {
            d('No roles for user ' + username);
            c(RC.OK);
        }
        else {
            return data ? JSON.parse(data) : {};
        }
        
        
    })
    .then(json => {
        let userroles = document.getElementById('userroles');
        
        // write the roles into the userroles span
        userroles.innerHTML = json;
    })
    .catch((error) => {
        console.error('Error:', error);
        d(error);
        c(RC.ERROR);
    });

}

function getUser() {
    console.log('getUser');

    if (islocal()) return;

    // Get the listid from the querystring
    let urlParams = new URLSearchParams(window.location.search);
    let username = urlParams.get('username');
    let userid = urlParams.get('userid');
    console.debug('GETUSER: ' + username);
    if ((username == null || username == '') && (userid == null || userid == '')) {
        return;
    }
    // assume this page is ON the server with the API
    // get the url of the API from the current page url
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edituser.html'));
    apiUrl = apiUrl + '/showusers/' + username;
    console.debug('GETUSER - calling: ' + apiUrl);
    // Get the list data from the API

    fetch(apiUrl, {headers: {
        "GeFeSLE-XMLHttpRequest": "true"
        }})
        .then(response => {
            if (response.status == RC.NOT_FOUND) {
                console.debug('GETUSER - User ' + username + ' not found!');
                throw new Error('User ' + username + ' not found!')
            }
            else if (response.status == RC.OK) {
                console.debug('GETUSER - User ' + username + ' retreived!');
                return response.json();
            }
            else if (response.status == RC.UNAUTHORIZED) {
                console.debug('GETUSER - Not authorized to get user ' + username);
                throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                console.debug('GETUSER - Forbidden to get user ' + username);
                throw new Error('Forbidden! Are you logged in as an admin?');
            }
            else {
                console.debug('GETUSER - Error getting user ' + username);
                throw new Error('Error getting user ' + username);
            }
        })
        .then(data => {
            // populate the form with the user data
            document.getElementById('userid').value = data.id;
            document.getElementById('username').value = data.userName;
            document.getElementById('email').value = data.email;
            d("User " + data.userName + " retreived!");
            c(RC.OK);
            getRoles();
        })
        .catch((error) => {
            console.error('Error:', error);
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
        .then(response => {
            // badrequest, no json -- username or newpassword were empty
            // but WE already trapped for that.
            // not found if username not found
            // ok = success
            // badrequest + json collection of code/description pairs if bad passwords
            // problem (500) for anything else
            if (response.ok) {
                d('Password for ' + userName + ' changed!');
                c(RC.OK);
                return;
            }
            else if (response.status == RC.NOT_FOUND) {
                throw new Error('User ' + userName + ' not found!');
            }
            else if (response.status == RC.UNAUTHORIZED) {
                throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                throw new Error('Forbidden! Are you logged in as an admin?');
            }   


            else if (response.status == RC.BAD_REQUEST) {

                response.json().then(data => {

                    // its gonna be a collection of code/description pairs
                    console.debug('CHANGEPASSWORD - bad password: ' + JSON.stringify(data));
                    // iterate over data - its going to be an array
                    let msg = '';
                    data.forEach((item) => {
                        msg += 'Error: ' + item.code + ' - ' + item.description + '<br>';
                    })
                    d(msg);
                    c(RC.BAD_REQUEST);

                });
            }
            else {
                throw new Error('Error changing password for ' + userName + ' - ' + response.status);
            }

        })

        .catch(error => {
            d(error);
            c(RC.ERROR);
            console.error('Error:', error);
        });
}



async function addUser(e) {
    e.preventDefault();
    if (islocal()) return;
    console.log('ADDUSER');
    let apiUrl = "";


    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edituser.html'));
    apiUrl = apiUrl + '/adduser';
    // construct the IdentityUser object
    let user = {
        userName: document.getElementById('username').value,
        email: document.getElementById('email').value,
        password: document.getElementById('password').value
    };

    console.info('ADDUSER -- Calling API: ' + apiUrl + ' with data: ' + JSON.stringify(user));
    fetch(apiUrl, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
        body: JSON.stringify(user)
    })
    .then(response => {
        //uname/pwd is null --> bad request
        //<created>-> 201.created
        //<not created>->bad request + collection <errors>
        //anything else  -> bad request/500
        if(response.ok){
            console.debug('ADDUSER - new user ADD ok!')
            d('User added:' + JSON.stringify(user));
            c(RC.CREATED);
            response.json().then(data => {
                // get the value of the radio button in the form
                let neworupdated = document.querySelector('input[name="neworupdate"]:checked').value;
                console.debug('ADDUSER | neworupdated: ' + neworupdated);
                if (neworupdated == 'new') {
                    // clear the form
                    document.getElementById('username').value = '';
                    document.getElementById('password').value = '';
                    document.getElementById('email').value = '';
                }
                else {
                    // now that we have newID, populdate the id field in the form
                    document.getElementById('userid').value = data.id;
                    document.getElementById('username').value = data.userName;
                    document.getElementById('password').value = '';
                    document.getElementById('email').value = data.email;
                
                    // and update the url in the browser to include the new username
                    // RECONSIDER THIS
                    let newUrl = window.location.href;
                    newUrl = newUrl.substring(0, newUrl.indexOf('?'));
                    newUrl = newUrl + '?username=' + data.userName;
                    window.history.pushState({}, '', newUrl);
                    console.debug('ADDUSER | New URL: ' + newUrl);
                }

            })
        }
        else if(response.status == RC.BAD_REQUEST){
            console.debug('ADDUSER - bad request');
            response.json().then(data => {
                let msg = '';
                data.forEach((item) => {
                    msg += 'Error: ' + item.code + ' - ' + item.description + '<br>';
                })
                d(msg);
                c(RC.BAD_REQUEST);
            });
        }
        else if (response.status == RC.UNAUTHORIZED) {
            console.debug('ADDUSER - Not authorized to add user ' + user.userName);
            throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
        }
        else if (response.status == RC.FORBIDDEN) {
            console.debug('ADDUSER - Forbidden to add user ' + user.userName);
            throw new Error('Forbidden! Are you logged in as an admin?');
        }
        else {
            console.debug('ADDUSER - Error adding user ' + user.userName);
            throw new Error('Error adding user ' + user.userName + ' - ' + response.status);
        }
    })
    .catch(error => {
        d(error);
        c(RC.ERROR);
        console.error('Error:', error);
    });  
}

async function modifyUser(e) {
    e.preventDefault();
    if (islocal()) return;
    console.log('MODIFYUSER');
    let apiUrl = "";

    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edituser.html'));
    apiUrl = apiUrl + '/modifyuser';
    let user = {
        id: document.getElementById('userid').value,
        userName: document.getElementById('username').value,
        email: document.getElementById('email').value,
        password: document.getElementById('password').value
    };

    console.info('MODIFYUSER | Calling API: ' + apiUrl + ' with data: ' + JSON.stringify(user));
    fetch(apiUrl, {
        method: 'PUT',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
        body: JSON.stringify(user)
    })
        .then(response => {
            // if the user is updated ok, we get a 200
            // if the user (by ID) doesn't exist, we'll get a 404
            // if anything else happens in /modifyuser, we'll get a bad request
            if (response.ok) {
                d('User ' + user.userName + ' modified!');
                c(RC.OK);
            }
            else if (response.status == 404) {
                d('User ' + user.userName + ' not found!');
                c(RC.NOTFOUND);
            }
            else if (response.status == RC.BAD_REQUEST) {
                response.json().then(data => {
                    // its gonna be a collection of code/description pairs
                    console.debug('MODIFYUSER - bad something: ' + JSON.stringify(data));
                    // iterate over data - its going to be an array
                    let msg = '';
                    data.forEach((item) => {
                        msg += 'Error: ' + item.code + ' - ' + item.description + '<br>';
                    })
                    d(msg);
                    c(RC.BAD_REQUEST);
                });
            }
            else if (response.status == RC.UNAUTHORIZED) {
                console.debug('MODIFYUSER - Not authorized to modify user ' + user.userName);
                throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                console.debug('MODIFYUSER - Forbidden to modify user ' + user.userName);
                throw new Error('Forbidden! Are you logged in as an admin?');
            }
            else {
                d('Error modifying user ' + user.userName);
                c(RC.ERROR);
            }


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

// add onClick event to the changepassword button
//document.getElementById('setPassword').addEventListener('click', changePassword);
//document.getElementById('setPassword').addEventListener('click', sendHARDReset);

// when the radio button neworupdate is changed from update to new, clear the form
document.getElementById('new').addEventListener('change', function () {
    document.getElementById('username').value = '';
    document.getElementById('password').value = '';
    document.getElementById('email').value = '';
    document.getElementById('userid').value = '';
});

// get the addRole button and add an event listener to it
document.getElementById('addRole').addEventListener('click', setRoles);
document.getElementById('deleteRole').addEventListener('click', deleteRole);



function getUsers() {
    console.log('GETUSERS');
    if(islocal()) return;

    // assume this page is ON the server with the API
    // get the url of the API from the current page url
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edituser.html'));
    apiUrl = apiUrl + '/showusers';
    console.debug('GETUSERS - calling: ' + apiUrl);

    // Get the list data from the API
    fetch(apiUrl, {headers: {
        "GeFeSLE-XMLHttpRequest": "true"

    }})
    .then(response => {
        if (response.status == RC.NOT_FOUND) {
            console.debug('GETUSERS - No users found!');
            throw new Error('No users found!');
        }
        else if (response.status == RC.OK) {
            console.debug('GETUSERS - Users retreived!');
            return response.json();
        }
        else if (response.status == RC.UNAUTHORIZED) {
            console.debug('GETUSERS - Not authorized to get users');
            throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
        }
        else if (response.status == RC.FORBIDDEN) {
            console.debug('GETUSERS - Forbidden to get users');
            throw new Error('Forbidden! Are you logged in as an admin?');
        }
        else {
            console.debug('GETUSERS - Error getting users');
            throw new Error('Error getting users');
        }
    })
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
            userlist += ' - email: ' + user.email + ' <-- <a href="/deleteuser/' + user.id +'">DELETE</a></li>';
        });
        userlist += '</ol>';
        users.innerHTML = userlist;
    })
    .catch((error) => {
        console.error('Error:', error);
        d(error);
        c(RC.ERROR);
    });



}

function deleteRole() {
    console.log('DELETEROLE');
    if(islocal()) return;

    // get the username from the form
    let username = document.getElementById('username').value;
    // get the selected role from the roles dropdown
    let role = document.getElementById('role').value;
    console.debug('DELETEROLE: ' + username + ' - ' + role);

    if(username == null || username == '' || role == null || role == ''){
        d('Cant delete role for user: user/role cannot be blank!');
        c(RC.ERROR);
    }

    // assume this page is ON the server with the API
    // get the url of the API from the current page url
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edituser.html'));
    
    // Get the list data from the API
    apiUrl = apiUrl + '/deleterole/' + username + '/' + role;
    console.debug('DELETEROLE - calling: ' + apiUrl);
    fetch(apiUrl, {headers: {
        "GeFeSLE-XMLHttpRequest": "true"
    }})
    .then(response => {
        if (response.status == RC.NOT_FOUND) {
            console.debug('DELETEROLE - User ' + username + ' not found!');
            throw new Error('User ' + username + ' not found!')
        }
        else if (response.status == RC.OK) {
            d('Role ' + role + 'for ' + username + ' UNASSIGNED!');
            c(RC.OK);
            // wait 3 seconds then call getRoles to refresh the roles list
            setTimeout(getRoles, 3000);
            return;
            
        }
        else if (response.status == RC.UNAUTHORIZED) {
            console.debug('DELETEROLE - Not authorized to set roles for user ' + username);
            throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
        }
        else if (response.status == RC.FORBIDDEN) {
            console.debug('DELETEROLE - Forbidden to get user ' + username);
            throw new Error('Forbidden! Are you logged in as an admin?');
        }
        else if (response.status == RC.BAD_REQUEST) {

            response.json().then(data => {

                // its gonna be a collection of code/description pairs
                console.debug('DELETEROLE - bad something: ' + JSON.stringify(data));
                // iterate over data - its going to be an array
                let msg = '';
                data.forEach((item) => {
                    msg += 'Error: ' + item.code + ' - ' + item.description + '<br>';
                })
                d(msg);
                c(RC.BAD_REQUEST);

            });
        }
        else {
            console.debug('DELETEROLE - Error Setting user roles' + username);
            throw new Error('Error Setting roles for ' + username);
        }
    })
    .catch((error) => {
        console.error('Error:', error);
        d(error);
        c(RC.ERROR);
    });



}