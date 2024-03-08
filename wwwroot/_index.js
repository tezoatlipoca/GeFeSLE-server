// define the onClick event handler function for the indexregenlink link
async function interceptRegen(event) {
    // prevent the default action of the link
    event.preventDefault();
    console.debug(' interceptRegen');

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
        }

    if(!await amLoggedIn()) {
        d("You are not logged in! <a href='_login.html'>Login here.</a>");
        c(RC.UNAUTHORIZED);
        return;
    }

    let regenlink = document.getElementById('indexregenlink');
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/index.html'));
    
    // Get the list data from the API
    apiUrl = apiUrl + '/regenerate';
    console.debug(' | REGEN - apiUrl: ', apiUrl);
    // fetch the /regenerate endpoint; the result will be some text; 
    // put the text into an alert
    // also returns: 
    await fetch(apiUrl, {
        method: 'GET',
        headers: {
            "GeFeSLE-XMLHttpRequest": "true"
        }
    })
        .then(response => {
            console.debug(' | REGEN - Response: ', response);
            if (response.status == RC.UNAUTHORIZED) {
                throw new Error('Not authorized! Have you logged in yet <a href=\"_login.html\">(click here)?</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                throw new Error('Forbidden! Are you logged in as an listowner?');
                
            }
            else {
                return response.text();
            }
            
            
        })
        .then((text) => {
            d(text);
            c(RC.OK);
            // wait 10 seconds and refresh the page
            setTimeout(() => { location.reload(); }, 10000);
        })
        .catch((error) => {
            console.error('Error:', error);
            d(error);
            c(RC.ERROR);
        });

}

window.onload = async function () {
    console.debug('index.js - window.onload')

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
        }

    let loggedIn = await amLoggedIn();
    console.debug(' | *loggedIn: ' + loggedIn);
    let role = await getRole();
    console.debug(' | *role: ' + role);

    


    if((isSuperUser(role) || isListOwner(role)) && loggedIn ) {
        
        console.debug(' | isSuperUser or isListOwner - and logged in');
    }
    else {
        // hide the id=indexeditlink
        let links = document.getElementsByClassName('indexeditlink');
        for (l of links) {
            l.style.display = 'none';
        }
        let regenlink = document.getElementById('indexregenlink');
        regenlink.style.display = 'none';

        if(!loggedIn) {
            d("<a href='_login.html'>Login here.</a>");
            c(RC.OK);
            return;
        }

    }
}


function deleteList(listId) {
    console.log('deleteList');
    if (islocal()) return;
    if (confirm('Are you sure you want to delete this LIST?')) {
        let apiUrl = 'deletelist/' + listId;
        fetch(apiUrl, {
            method: 'DELETE',
            headers: {
                'Content-Type': 'application/json',
                "GeFeSLE-XMLHttpRequest": "true"
            },
        })
            .then(response => {
                console.log('Response IS:', response);
                // if the response is ok, redirect to the list page
                if (response.ok) {
                    // save ourselves a result message in localstorage
                    localStorage.setItem('result','result '+ listId + ' deleted successfully');

                    // just refresh the page
                    location.reload();
                }
                else if (response.status == RC.UNAUTHORIZED) {
                    console.debug('DELETELIST - Not authorized to delete lists');
                    throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
                }
                else if (response.status == RC.FORBIDDEN) {
                    console.debug('DELETELIST - Forbidden to get user ' + username);
                    throw new Error('Forbidden! Are you logged in as an admin?');
                }
                else {
                    d('Error: ' + response.statusText);
                    c(RC.ERROR);
                }

            })
            .catch((error) => {
                console.error('Error:', error);
                d(error);
                c(RC.ERROR);
            });
    }
}