<p align="center">
  <img
    src="http://res.cloudinary.com/vidsy/image/upload/v1503160820/CoZ_Icon_DARKBLUE_200x178px_oq0gxm.png"
    width="125px;">
</p>

<h1 align="center">neo-fund</h1>

<p align="center">
  Decentralised Funding platform on the <b>NEO</b> blockchain.
</p>

# Contract
Hash: 0x76dfef16ed0427ab26d970dee2a2e275ce526354


## Overview
Neo Fund is a decentralized funding platform, similar to kickstarter.. The basic function is to set a goal amount, and date limit. If the goal is reached the creator of the fund will be awarded the funds, and if not the contributors can redeem their funds again..


## Smart Contract Invokable Operations

#### Creator:

- CreateFund

#### Contributor:

- DepositFunds
- GetFundParameter
- ReachedGoal
- ReachedEndTime
- IsRefundActive
- GetContributorInfo
- GetFundsOwed



# Using Neo Fund

### Using neo-python:
Copy `neo-fund-py\neo-fund-prompt.py` into the `neo-python` root directory and run `python neo-fund-prompt.py -c protocol.testnet.json` instead of `python prompt.py -c protocol.testnet.json` and then use tab complete to see all the options.

### Manual invoke:


#### Process:
The contract input looks like this, so all operations are called by the first string, followed by an array of arguments
```
Main(string operation, params object[] args)
```
#### CreateFund
Create a new fund by invoking the following (duplicate will return false)

```
"CreateFund",[byte[] creatorSH, string fid, byte[] asset, byte[] withdrawalSH, BigInteger goal, BigInteger endtime]
```

#### DepositFunds
Contributors can now deposit to the fund, it is required that you send any assets and invoke this operation in the same transaction.

```
"DepositFunds",[string fid, byte[] asset, byte[] contributorSH]
```

#### GetFundParameter
Once a fund is created, you would invoke this operation to check the status or information.
param input options are:

- creatorSH
- asset
- withdrawalSH
- goal
- endtime
- fundBalance

```
"GetFundParameter",[string fid, string param]
```

#### Withdrawing Funds
Once the fund has either completed or failed either the Creator or the Contributors can withdraw depending on the result. Either way the same method is used.

First; it is required to request to unlock the funds.
```
"WithdrawFundsRequest",[string fid, byte[] requestorSH, BigInteger requestedAmount]
```

Second; You will have funds unlocked if you are either the Creator or the Contributor. So you can send a withdraw transaction

Third; It is currently required to invoke a WithdrawRequestReset method do avoid double spending.
```
"WithdrawRequestReset",[byte[] requestorSH]
```




#### Test TX (Creating fund)

https://neoscan-testnet.io/transaction/2548ddb8aac6c18cd916358da4cea21da009d9afb312a318f16de001d40539a6
