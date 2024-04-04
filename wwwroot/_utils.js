function islocal() {
    // determine if this page is a local file i.e. file://
    let isLocal = window.location.protocol == 'file:';
    console.debug(' | isLocal: ' + isLocal);
    if (isLocal) {
        d('Cannot call API from a local file!');
        c(RC.BAD_REQUEST);
        // disable the form submit action
        document.getElementById('editlistform').addEventListener('submit', function (e) {
            e.preventDefault();
        });
        // make the form fields readonly and greyed out
        document.getElementById('list.name').readOnly = true;
        document.getElementById('list.comment').readOnly = true;
        return true;
    } else
        return false;

}

// method for feedback to page viewer user
// function that takes the provides string and writes it into the 
// span with id="result"
// if no span with id="result" exists, then this function writes to the console instead
function d(result) {
    // get the span with id="result"
    let resultSpan = document.getElementById('result');
    if (resultSpan == null) {
        // if no span with id="result" exists, then write to the console
        console.log('No span with id="result" found on this page!')
        console.log(result);
        return;
    } else {

        document.getElementById('result').innerHTML = result;
    }

}

// create an enum
const RC = {
    OK: 200,
    CREATED: 201,
    NO_CONTENT: 204,
    BAD_REQUEST: 400,
    NOT_FOUND: 404,
    UNAUTHORIZED: 401,
    FORBIDDEN: 403,
    CONFLICT: 409,
    INTERNAL_SERVER_ERROR: 500,
    ERROR: 999
};

function c(RC) {

    let resultSpan = document.getElementById('result');
    if (resultSpan == null) {
        // if no span with id="result" exists, then write to the console
        console.log('No span with id="result" found on this page!')
        console.log(result);
        return;
    } else {
        // change the color of the result span based on the RC
        switch (RC) {
            case 200:
                resultSpan.style.color = 'green';
                break;
            case 201:
                resultSpan.style.color = 'green';
                break;
            case 204:
                resultSpan.style.color = 'green';
                break;
            case 400:
                resultSpan.style.color = 'red';
                break;
            case 401:
                resultSpan.style.color = 'red';
                break;
            case 404:
                resultSpan.style.color = 'red';
                break;
            case 409:
                resultSpan.style.color = 'red';
                break;
            case 500:
                resultSpan.style.color = 'red';
                break;
            case 999:
                resultSpan.style.color = 'red';
                break;
            default:
                resultSpan.style.color = 'black';
                break;
        }
    }


}


// we don't want to hit the backend for every page load.
// on a successful login, we'll store the role in local storage. 
// we'll have to rely on backend .NET core authentication
// to ensure that a) the user session/token is still good and b)
// the user still has the same role, or at least sufficient role 
// to do the thing. 
// so really all this does is return what's in local storage.


async function getRole() {
    console.info('getRole');
    let role = 'anonymous';

    // get the role from local storage
    role = localStorage.getItem('role');
    if (Boolean(role)) {
        console.debug(' | GETROLE - Role found in local storage: ' + role);
        return role;
    }
    else {
        // we will ONCE to get the role from the server
        // and then store it in local storage
        let apiUrl = '/getmyrole';
        await fetch(apiUrl, {
            method: 'GET',
            headers: {
                'Content-Type': 'application/json',
                "GeFeSLE-XMLHttpRequest": "true"
            },
        })
        .then(response => {
            if(response.ok) {
                return response.text();
            }
            else if (response.status == RC.UNAUTHORIZED) {
                console.debug(' | GETMYROLE - Not authorized to get role');
                throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                console.debug(' | GETMYROLE - Forbidden to get role');
                throw new Error('Forbidden! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
            }
            else {
                throw new Error(' | GETMYROLE - url: ' + apiUrl + ' returned: ' + response.status + ' - ' + response.statusText);
            }})
        .then(text => {
            text = text.replace(/^"|"$/g, ''); // Remove quotation marks at the start and end of the string
            console.log('GETROLE Response IS:', text);
            localStorage.setItem('role', text);
            return text;
        })
        .catch((error) => {
                console.error('Error:', error);
                d(error);
                c(RC.ERROR);
                return role;
            });


        
    }

}

function showDebuggingElements() {
    let fn = "showDebuggingElements";
    console.info(fn);
    let links = document.getElementsByClassName('debugging');
        for (l of links) {
            l.style.display = '';
        }
}

function showListSecrets() {
    let fn = "showListSecrets";
    console.info(fn);
    let links = document.getElementsByClassName('editlink');
    for (let l of links) { l.style.display = ''; }
    links = document.getElementsByClassName('edititemlink');
    for (let l of links) { l.style.display = ''; }
    links = document.getElementsByClassName('mastoimportlink');
    for (let l of links) { l.style.display = ''; }
    links = document.getElementsByClassName('itemeditlink');
    for (let l of links) { l.style.display = ''; }
    links = document.getElementsByClassName('itemdeletelink');
    for (let l of links) { l.style.display = ''; }
    links = document.getElementsByClassName('moveitemlink');
    for (let l of links) { l.style.display = ''; }
    links = document.getElementsByClassName('stickynoteslink');
    for (let l of links) { l.style.display = ''; }
    
}

// convenience routines for the above that return true
// if 

function isListOwner(r) {
    console.info('isListOwner');
    if (r === 'listowner') {
        return true;
    }
    else {
        return false;
    }
}

function isContributor(r) {
    console.info('isContributor');
    if (r === 'contributor') {
        return true;
    }
    else {
        return false;
    }
}

function isSuperUser(r) {
    console.info('isSuperUser');
    if (r === 'SuperUser') {
        return true;
    }
    else {
        return false;
    }
}

// similar to getRole, this function "attempts" to see if the user is logged in or not. 
// WHAT that entails is encapsulated here. The point is to have a boolean we can easily check
// before exposing any EDIT fields - don't want the user to make tons of changes then have 
// the submit fail because they're not logged in, then they have to re-enter everything.

async function amloggedin() {
    let fn = 'amLoggedIn';
    console.info(fn);
    
    
    // check to see if our jwt token is in local storage
    let token = localStorage.getItem('apiToken');
    if (Boolean(token)) {
        console.debug(fn + ' | Token found in local storage');
        return;
    }
    else {
        console.debug(fn + ' | Token NOT found in local storage');
        
    }

    // we can't check the existance of either an auth or session cookie
    // because we set those to httpOnly server side. 
    // we have to hit the backened to see if the user has either a valid
    // jwt token or auth session

    try {
        let serverUrl = window.location.origin;
        apiUrl = serverUrl + '/amloggedin/'
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
                    throw new Error('GeFeSLE Server: ' + storconfig.url + ' Not Found - check your settings');
                }
                else if (response.status == RC.UNAUTHORIZED) {
                    throw new Error('Not authorized - have you logged in yet? <a href="' + storconfig.url + '/login">Login</a>');
                }
                else if (response.status == RC.FORBIDDEN) {
                    throw new Error('Forbidden - have you logged in yet? <a href="' + storconfig.url + '/login">Login</a>');
                }
                else {
                    throw new Error('Error ' + response.status + ' - ' + response.statusText);
                }

            })
            .then(json => {
                let username = json.username;
                let role = json.userClaimsRole;
                console.debug(fn + ' | API Response: ' + JSON.stringify(json));
                if ((username == null) || (role == null)) {
                    console.debug(fn + ' | returning null value!');
                    return [null, null];
                }
                else {
                    console.debug(fn + ' | returning [ ' + username + ',' + role + ' ]');
                    return [username, role];
                }
                
            });
    } catch (error) {
        console.error('Error:', error);
        d('Error: ' + error);
        c(RC.ERROR);
    }
}

// utility function for List view - if col1 cell contains what appears to be a URL, make it clicky

function make1stcelllinks() {
    console.debug('make1stcelllinks')
    document.querySelectorAll('tr').forEach(function (tr) {
        var td = tr.querySelector('td');
        var text = td.textContent;
        var urlPattern = /^\s*(http|https):\/\/[^ "]+\s*$/;

        if (urlPattern.test(text)) {

            var a = document.createElement('a');
            a.href = text;
            a.textContent = text;

            while (td.firstChild) {
                td.removeChild(td.firstChild);
            }

            td.appendChild(a);
        }
    });
}

const GeListVisibility =  
{
    Public: 0,          // anyone can view the list's html page, json, rss etc. 
    Contributors: 1,    // restricted to contributors and list owners (and list creator and SU)
    ListOwners: 2,      // restricted to only list owners (and creator and SU)
    Private: 3          // restricted to only creator and SU
}
