function islocal() {
    // determine if this page is a local file i.e. file://
    let isLocal = window.location.protocol == 'file:';
    console.debug('>isLocal: ' + isLocal);
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
    links = document.getElementsByClassName('googletaskslink');
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
        apiUrl = serverUrl + '/me/'
        console.debug(fn + ' | API URL: ' + apiUrl);

        // Perform a GET request
        return await fetch(apiUrl, {
            headers: {
                "GeFeSLE-XMLHttpRequest": "true",
            }
        })
            .then(handleResponse)
            .then(response => { return response.json();})
            .then(json => {
                let id = json.id;
                let userName = json.userName;
                let role = json.role;
                console.debug(fn + ' | API Response: ' + JSON.stringify(json));

                return [id, userName, role];
            }
            );
    } catch (error) {
        console.error(error);
        d(error);
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


function handleResponse(response) {
    if (!response.ok) {
        let contentType = response.headers.get("Content-Type");

        if (contentType != null && contentType.includes("application/json")) {
            // The response is JSON
            return response.json().then(errorDetails => {
                // Check if errorDetails is an object
                if (typeof errorDetails === 'object' && errorDetails !== null) {
                    let errorDetailsString = '';
                    for (let key in errorDetails) {
                        if (typeof errorDetails[key] === 'object' && errorDetails[key] !== null) {
                            for (let subKey in errorDetails[key]) {
                                errorDetailsString += ` ${errorDetails[key][subKey]}<br>\n`;
                            }
                        } else {
                            errorDetailsString += `${key}: ${errorDetails[key]}<br>\n`;
                        }
                    }
                    return Promise.reject(new Error(errorDetailsString));
                } else {
                    // errorDetails is not an object, return it as the error message
                    return Promise.reject(new Error(errorDetails));
                }
            });
        }
        else if (contentType != null && contentType.includes("text/plain")) {
            // The response is text
            return response.text().then(errorDetails => {
                throw new Error(`${response.status} - ${response.statusText} - ${response.url} - ${errorDetails}`);
            });
        }
        else {
            // The response is some other type
            throw new Error(`${response.status} - ${response.statusText} - ${response.url}`);
        }
    }
    return response;
}